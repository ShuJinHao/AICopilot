using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.AiGatewayService.Agents;

public class ApprovalDecisionStreamHandler(
    IReadRepository<Session> sessionRepository,
    ICurrentUser currentUser,
    IAuditLogWriter auditLogWriter,
    ConfiguredAgentRuntimeFactory configuredAgentRuntimeFactory,
    MainChatToolCatalog mainChatToolCatalog,
    IAgentSessionStateStore agentSessionStateStore,
    SessionMessagePersistenceService messagePersistenceService,
    ISessionExecutionLock sessionExecutionLock,
    IChatExecutionMetadataAccessor executionMetadataAccessor,
    IAgentStreamRuntime chatStreamRuntime)
    : IStreamRequestHandler<ApprovalDecisionStreamRequest, ChatChunk>
{
    private static readonly JsonSerializerOptions AgentSessionJsonOptions =
        new(JsonSerializerDefaults.Web);

    public async IAsyncEnumerable<ChatChunk> Handle(
        ApprovalDecisionStreamRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var assistantText = new StringBuilder();
        var renderChunks = new List<ChatChunk>();
        var pendingMessages = new List<SessionMessageAppend>();
        var session = await chatStreamRuntime.LoadSessionAsync(
            sessionRepository,
            request.SessionId,
            ct);
        if (session is null)
        {
            yield return AgentStreamRuntime.CreateErrorChunk(
                "session_not_found",
                "未找到对应的会话。",
                nameof(ApprovalDecisionStreamHandler),
                "当前会话不存在或已被删除，请刷新后重试。");
            yield break;
        }

        if (currentUser.Id != session.UserId)
        {
            yield return AgentStreamRuntime.CreateErrorChunk(
                AuthProblemCodes.MissingPermission,
                "当前用户无权操作该会话。",
                nameof(ApprovalDecisionStreamHandler),
                "当前账号无权操作该会话。");
            yield break;
        }

        await using var sessionLock = await sessionExecutionLock.AcquireAsync(
            request.SessionId,
            ct);
        var turnId = Guid.NewGuid();
        AgentSessionStateSnapshot? state = null;
        Exception? failure = null;
        try
        {
            state = await agentSessionStateStore.BeginTurnAsync(
                request.SessionId,
                session.UserId,
                currentUser.CloudTenantId,
                turnId,
                approvalContinuation: true,
                ct);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (failure is not null)
        {
            yield return ChatStreamHandler.CreateSessionStateErrorChunk(
                failure,
                nameof(ApprovalDecisionStreamHandler));
            yield break;
        }

        if (AgentApprovalBindingCollector.HasMultipleDifferentToolCalls(
                state!.PendingApprovals))
        {
            await TryInterruptAsync(session, turnId);
            yield return ChatStreamHandler.CreateSessionStateErrorChunk(
                new AgentRuntimeMultipleToolCallsException(),
                nameof(ApprovalDecisionStreamHandler));
            yield break;
        }

        var binding = state.PendingApprovals.FirstOrDefault(item =>
            string.Equals(item.ToolCallId, request.CallId, StringComparison.Ordinal));
        if (binding is null)
        {
            await CompleteWithoutModelAsync(state, turnId, ct);
            yield return AgentStreamRuntime.CreateErrorChunk(
                AppProblemCodes.ApprovalAlreadyProcessed,
                "该审批请求已处理或已失效。",
                nameof(ApprovalDecisionStreamHandler),
                "该审批请求已处理或已失效，请重新发起新的请求。");
            yield break;
        }

        var validation = ApprovalDecisionValidator.Validate(
            request,
            binding,
            assistantText);
        if (!validation.IsValid)
        {
            await CompleteWithoutModelAsync(state, turnId, ct);
            yield return validation.Error!;
            yield break;
        }

        var expectedDigest = CanonicalJson.ComputeSha256(
            CanonicalJson.Serialize(binding.Arguments));
        if (!string.Equals(
                expectedDigest,
                binding.CanonicalArgumentsDigest,
                StringComparison.Ordinal))
        {
            await agentSessionStateStore.InterruptTurnAsync(
                request.SessionId,
                session.UserId,
                currentUser.CloudTenantId,
                turnId,
                CancellationToken.None);
            yield return AgentStreamRuntime.CreateErrorChunk(
                AppProblemCodes.AgentSessionResetRequired,
                "The approval arguments binding is corrupt.",
                nameof(ApprovalDecisionStreamHandler),
                "审批上下文校验失败，当前会话需要重建。");
            yield break;
        }

        AiToolDefinition[] tools = [];
        var trustedRenderChunks = new TrustedRenderChunkBuffer();
        try
        {
            tools = await mainChatToolCatalog.BuildAsync(
                session,
                string.Empty,
                trustedRenderChunks,
                ct);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (failure is not null)
        {
            await TryInterruptAsync(session, turnId);
            if (!ct.IsCancellationRequested)
            {
                yield return ChatStreamHandler.CreateSessionStateErrorChunk(
                    failure,
                    nameof(ApprovalDecisionStreamHandler));
            }

            yield break;
        }

        var definition = tools.SingleOrDefault(tool =>
            string.Equals(tool.Name, binding.ToolName, StringComparison.Ordinal));
        if (definition is null ||
            !definition.RequiresApproval ||
            definition.SchemaVersion != binding.ToolSchemaVersion)
        {
            await agentSessionStateStore.InterruptTurnAsync(
                request.SessionId,
                session.UserId,
                currentUser.CloudTenantId,
                turnId,
                CancellationToken.None);
            yield return AgentStreamRuntime.CreateErrorChunk(
                AppProblemCodes.ApprovalAlreadyProcessed,
                "The approved tool identity or schema version changed.",
                nameof(ApprovalDecisionStreamHandler),
                "工具定义已变化，本次审批已失效，请重新发起请求。");
            yield break;
        }

        Exception? auditFailure = null;
        try
        {
            var auditedToolName = FormatToolName(binding);
            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Approval,
                    validation.IsApproved ? "Approval.Approve" : "Approval.Reject",
                    "ToolApproval",
                    binding.ToolCallId,
                    auditedToolName,
                    validation.IsApproved ? AuditResults.Succeeded : AuditResults.Rejected,
                    validation.IsApproved
                        ? $"Approval accepted: {auditedToolName}."
                        : $"Approval rejected: {auditedToolName}."),
                ct);
            await auditLogWriter.SaveChangesAsync(ct);
        }
        catch (Exception exception)
        {
            auditFailure = exception;
        }

        if (auditFailure is not null)
        {
            await TryInterruptAsync(session, turnId);
            if (!ct.IsCancellationRequested)
            {
                yield return ChatStreamHandler.CreateSessionStateErrorChunk(
                    auditFailure,
                    nameof(ApprovalDecisionStreamHandler));
            }

            yield break;
        }

        pendingMessages.Add(new SessionMessageAppend(
            AgentStreamRuntime.BuildApprovalSummary(
                validation.Identity is null
                    ? validation.ToolName
                    : $"{validation.Identity.TargetName}/{validation.Identity.ToolName}",
                validation.IsApproved),
            MessageType.User));

        var checkpointSink = new AgentSessionCheckpointSink(
            agentSessionStateStore,
            request.SessionId,
            session.UserId,
            currentUser.CloudTenantId,
            turnId);
        ScopedRuntimeAgent? scopedRuntime = null;
        IHarnessRuntimeChatAgent? harnessAgent = null;
        IRuntimeAgentSession? harnessSession = null;
        try
        {
            scopedRuntime = await configuredAgentRuntimeFactory.CreateHarnessAgentAsync(
                new ConversationTemplateId(session.TemplateId),
                tools,
                checkpointSink,
                ct);
            var configurationSnapshot = scopedRuntime.ConfigurationSnapshot
                ?? throw new InvalidOperationException(
                    "Approval continuation did not return its effective model configuration.");
            executionMetadataAccessor.SetFinalConfiguration(
                configurationSnapshot);
            harnessAgent = scopedRuntime.Agent as IHarnessRuntimeChatAgent
                ?? throw new InvalidOperationException(
                    "Approval continuation did not create a Harness agent.");
            harnessSession = await harnessAgent.DeserializeSessionAsync(
                state.SerializedSessionState,
                AgentSessionJsonOptions,
                ct);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (failure is not null)
        {
            if (scopedRuntime is not null)
            {
                await scopedRuntime.DisposeAsync();
            }

            await TryInterruptAsync(session, turnId);
            if (!ct.IsCancellationRequested)
            {
                yield return ChatStreamHandler.CreateSessionStateErrorChunk(
                    failure,
                    nameof(ApprovalDecisionStreamHandler));
            }

            yield break;
        }

        await using var runtimeLease = scopedRuntime!;
        var modelMetadata = AgentStreamRuntime.CreateMetadataChunk(
            scopedRuntime!.ConfigurationSnapshot!,
            "HarnessAgent");
        if (modelMetadata is not null)
        {
            renderChunks.Add(modelMetadata);
            yield return modelMetadata;
        }

        var runtimeApproval = new AiToolApprovalRequest(
            binding.RequestId,
            new AiToolCall(
                binding.ToolCallId,
                binding.ToolName,
                binding.ToolKind,
                binding.ServerName,
                binding.Arguments,
                binding.TargetType,
                binding.TargetName,
                binding.CanonicalToolName));
        var input = new AiChatMessage(
            AiChatRole.User,
            [
                new AiToolApprovalResponseContent(
                    runtimeApproval,
                    validation.IsApproved,
                    validation.IsApproved ? "approved" : "rejected")
            ]);
        var nextApprovals = new List<AgentApprovalBinding>();
        await using var updates = harnessAgent!.RunStreamingAsync(
                [input],
                harnessSession!,
                cancellationToken: ct)
            .GetAsyncEnumerator(ct);
        while (failure is null)
        {
            bool hasNext;
            try
            {
                hasNext = await updates.MoveNextAsync();
            }
            catch (Exception exception)
            {
                failure = exception;
                break;
            }

            if (!hasNext)
            {
                break;
            }

            var update = updates.Current;
            try
            {
                AgentApprovalBindingCollector.Capture(
                    update,
                    tools,
                    session,
                    currentUser.CloudTenantId,
                    nextApprovals);
            }
            catch (Exception exception)
            {
                failure = exception;
                break;
            }

            await using var chunks = chatStreamRuntime.CreateUpdateChunksAsync(
                    update,
                    "HarnessAgent",
                    session,
                    assistantText,
                    appendAssistantText: true,
                    ct)
                .GetAsyncEnumerator(ct);
            while (failure is null)
            {
                bool hasChunk;
                try
                {
                    hasChunk = await chunks.MoveNextAsync();
                }
                catch (Exception exception)
                {
                    failure = exception;
                    break;
                }

                if (!hasChunk)
                {
                    break;
                }

                var chunk = chunks.Current;
                renderChunks.Add(chunk);
                yield return chunk;
            }

            foreach (var trustedChunk in trustedRenderChunks.Drain())
            {
                renderChunks.Add(trustedChunk);
                yield return trustedChunk;
            }
        }

        foreach (var trustedChunk in trustedRenderChunks.Drain())
        {
            renderChunks.Add(trustedChunk);
            yield return trustedChunk;
        }

        ChatChunk? stateMetadata = null;
        if (failure is null)
        {
            try
            {
                var serialized = await harnessAgent.SerializeSessionAsync(
                    harnessSession!,
                    AgentSessionJsonOptions,
                    ct);
                var completed = await agentSessionStateStore.CompleteTurnAsync(
                    request.SessionId,
                    session.UserId,
                    currentUser.CloudTenantId,
                    turnId,
                    serialized,
                    nextApprovals
                        .DistinctBy(item => item.ToolCallId, StringComparer.Ordinal)
                        .ToArray(),
                    ct);
                var mode = await harnessAgent.GetModeAsync(harnessSession!, ct);
                stateMetadata = new ChatChunk(
                    "HarnessAgent",
                    ChunkType.AgentEvent,
                    JsonSerializer.Serialize(new
                    {
                        stage = "agent_session_state",
                        detail = "Harness AgentSession state persisted.",
                        recoverable = true,
                        metadata = new Dictionary<string, string>(),
                        sessionId = request.SessionId,
                        mode = mode == RuntimeAgentMode.Execute ? "execute" : "plan",
                        status = completed.Status.ToString(),
                        version = completed.Version,
                        pendingApproval = completed.PendingApprovals.Count > 0
                    }));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        if (stateMetadata is not null)
        {
            renderChunks.Add(stateMetadata);
            yield return stateMetadata;
        }

        if (failure is not null)
        {
            await TryInterruptAsync(session, turnId);
            if (ct.IsCancellationRequested)
            {
                yield break;
            }

            var error = ChatStreamHandler.CreateSessionStateErrorChunk(
                failure,
                nameof(ApprovalDecisionStreamHandler));
            renderChunks.Add(error);
            yield return error;
        }

        if (assistantText.Length > 0 || renderChunks.Count > 0)
        {
            pendingMessages.Add(new SessionMessageAppend(
                assistantText.Length > 0 ? assistantText.ToString() : null,
                MessageType.Assistant,
                executionMetadataAccessor.ToMessageSnapshot(),
                renderChunks));
        }

        if (pendingMessages.Count > 0)
        {
            await messagePersistenceService.AppendBatchAsync(
                request.SessionId,
                pendingMessages,
                ct);
        }
    }

    private async Task CompleteWithoutModelAsync(
        AgentSessionStateSnapshot state,
        Guid turnId,
        CancellationToken cancellationToken)
    {
        _ = await agentSessionStateStore.CompleteTurnAsync(
            state.SessionId,
            state.UserId,
            state.TenantId,
            turnId,
            state.SerializedSessionState,
            state.PendingApprovals,
            cancellationToken);
    }

    private async Task TryInterruptAsync(
        SessionRuntimeSnapshot session,
        Guid turnId)
    {
        try
        {
            await agentSessionStateStore.InterruptTurnAsync(
                session.Id,
                session.UserId,
                currentUser.CloudTenantId,
                turnId,
                CancellationToken.None);
        }
        catch (AgentSessionStateException)
        {
        }
    }

    private static string FormatToolName(AgentApprovalBinding binding)
    {
        return binding.TargetType is null ||
               string.IsNullOrWhiteSpace(binding.TargetName) ||
               string.IsNullOrWhiteSpace(binding.CanonicalToolName)
            ? binding.ToolName
            : $"{binding.TargetType}:{binding.TargetName}/{binding.CanonicalToolName}";
    }

}
