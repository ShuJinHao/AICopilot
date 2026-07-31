using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.AiGatewayService.Agents;

/// <summary>
/// The sole HTTP/SSE main-chat path. The legacy AgentWorkflowPipeline remains
/// compiled for B2 compatibility, but is intentionally unreachable here.
/// </summary>
public class ChatStreamHandler(
    IReadRepository<Session> sessionRepository,
    ICurrentUser currentUser,
    ConfiguredAgentRuntimeFactory configuredAgentRuntimeFactory,
    MainChatToolCatalog mainChatToolCatalog,
    IAgentSessionStateStore agentSessionStateStore,
    SessionMessagePersistenceService messagePersistenceService,
    IOperationalBoundaryPolicy operationalBoundaryPolicy,
    IManufacturingSceneClassifier sceneClassifier,
    ISessionExecutionLock sessionExecutionLock,
    IAgentExecutionMetadataAccessor executionMetadataAccessor,
    IAgentStreamRuntime chatStreamRuntime)
    : IStreamRequestHandler<ChatStreamRequest, ChatChunk>
{
    private static readonly JsonSerializerOptions AgentSessionJsonOptions =
        new(JsonSerializerDefaults.Web);

    public async IAsyncEnumerable<ChatChunk> Handle(
        ChatStreamRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var assistantText = new StringBuilder();
        var assistantRenderChunks = new List<ChatChunk>();
        var pendingMessages = new List<SessionMessageAppend>();
        var session = await LoadSessionSafelyAsync(request.SessionId, ct);
        if (session.Error is not null)
        {
            yield return session.Error;
            yield break;
        }

        if (session.Value is null)
        {
            yield break;
        }

        var runtimeSession = session.Value!;
        if (currentUser.Id != runtimeSession.UserId)
        {
            yield return AgentStreamRuntime.CreateErrorChunk(
                AuthProblemCodes.MissingPermission,
                "当前用户无权操作该会话。",
                nameof(ChatStreamHandler),
                "当前账号无权操作该会话。");
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            pendingMessages.Add(new SessionMessageAppend(request.Message, MessageType.User));
        }

        var sceneDecision = sceneClassifier.Classify(request.Message);
        var blockedByPolicy = operationalBoundaryPolicy.TryBlockControlRequest(
            request.Message,
            out var policyDecision);
        if (sceneDecision.Scene == ManufacturingSceneType.ControlBlocked || blockedByPolicy)
        {
            var boundary = policyDecision ?? new OperationalBoundaryDecision(
                AppProblemCodes.ControlActionBlocked,
                "AICopilot 只提供观察、诊断、建议和知识问答，不执行任何控制动作。",
                "我不能直接执行重启、写参数、下发配方、写入 PLC 或状态切换。如果需要，我可以继续给出诊断结论、风险提示和人工执行前检查项。");
            assistantText.Append(boundary.UserFacingMessage);
            var blockedChunk = AgentStreamRuntime.CreateErrorChunk(
                boundary.Code,
                boundary.Detail,
                "OperationalBoundaryPolicy",
                boundary.UserFacingMessage);
            assistantRenderChunks.Add(blockedChunk);
            yield return blockedChunk;
            await PersistMessagesAsync(
                request.SessionId,
                assistantText,
                assistantRenderChunks,
                pendingMessages,
                ct);
            yield break;
        }

        IAsyncDisposable? sessionLock = null;
        ChatChunk? lockError = null;
        try
        {
            sessionLock = await sessionExecutionLock.AcquireAsync(request.SessionId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            yield break;
        }
        catch (Exception exception)
        {
            lockError = AgentStreamRuntime.CreateErrorChunk(
                exception,
                nameof(ChatStreamHandler),
                AppProblemCodes.ChatStreamFailed,
                "对话执行失败，请稍后重试。");
        }

        if (lockError is not null)
        {
            yield return lockError;
            yield break;
        }

        if (sessionLock is null)
        {
            yield break;
        }

        await using (sessionLock)
        {
            var turnId = Guid.NewGuid();
            AgentSessionStateSnapshot? state = null;
            ScopedRuntimeAgent? scopedRuntime = null;
            IRuntimeAgentSession? harnessSession = null;
            IHarnessRuntimeChatAgent? harnessAgent = null;
            AiToolDefinition[] tools = [];
            Exception? failure = null;
            ChatChunk? setupError = null;

            try
            {
                state = await agentSessionStateStore.BeginTurnAsync(
                    request.SessionId,
                    runtimeSession.UserId,
                    currentUser.CloudTenantId,
                    turnId,
                    approvalContinuation: false,
                    ct);
                tools = await mainChatToolCatalog.BuildAsync(
                    runtimeSession,
                    request.Message,
                    ct);
                var checkpointSink = new AgentSessionCheckpointSink(
                    agentSessionStateStore,
                    request.SessionId,
                    runtimeSession.UserId,
                    currentUser.CloudTenantId,
                    turnId);
                scopedRuntime = await configuredAgentRuntimeFactory.CreateHarnessAgentAsync(
                    new ConversationTemplateId(runtimeSession.TemplateId),
                    tools,
                    checkpointSink,
                    ct);
                harnessAgent = scopedRuntime.Agent as IHarnessRuntimeChatAgent
                    ?? throw new InvalidOperationException(
                        "Main chat runtime did not return a Harness agent.");
                harnessSession = await harnessAgent.DeserializeSessionAsync(
                    state.SerializedSessionState,
                    AgentSessionJsonOptions,
                    ct);
            }
            catch (Exception exception)
            {
                failure = exception;
                setupError = CreateSessionStateErrorChunk(exception, nameof(ChatStreamHandler));
            }

            if (setupError is not null)
            {
                if (scopedRuntime is not null)
                {
                    try
                    {
                        await scopedRuntime.DisposeAsync();
                    }
                    catch (Exception exception)
                    {
                        failure ??= exception;
                        setupError = CreateSessionStateErrorChunk(
                            failure,
                            nameof(ChatStreamHandler));
                    }
                }

                if (state?.ActiveTurnId == turnId)
                {
                    await TryInterruptAsync(
                        request.SessionId,
                        runtimeSession.UserId,
                        currentUser.CloudTenantId,
                        turnId);
                }

                yield return setupError;
                yield break;
            }

            await using (scopedRuntime!)
            {
                var approvals = new List<AgentApprovalBinding>();
                await using var updates = harnessAgent!
                    .RunStreamingAsync(request.Message, harnessSession!, cancellationToken: ct)
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
                        CaptureApprovalBindings(
                            update,
                            tools,
                            runtimeSession,
                            currentUser.CloudTenantId,
                            approvals);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        break;
                    }

                    await using var chunks = chatStreamRuntime.CreateUpdateChunksAsync(
                            update,
                            "HarnessAgent",
                            runtimeSession,
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
                        assistantRenderChunks.Add(chunk);
                        yield return chunk;
                    }
                }

                AgentSessionStateSnapshot? completedState = null;
                RuntimeAgentMode? completedMode = null;
                if (failure is null)
                {
                    try
                    {
                        var serialized = await harnessAgent.SerializeSessionAsync(
                            harnessSession!,
                            AgentSessionJsonOptions,
                            ct);
                        completedMode = await harnessAgent.GetModeAsync(harnessSession!, ct);
                        completedState = await agentSessionStateStore.CompleteTurnAsync(
                            request.SessionId,
                            runtimeSession.UserId,
                            currentUser.CloudTenantId,
                            turnId,
                            serialized,
                            approvals
                                .DistinctBy(binding => binding.ToolCallId, StringComparer.Ordinal)
                                .ToArray(),
                            ct);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                }

                if (failure is not null)
                {
                    await TryInterruptAsync(
                        request.SessionId,
                        runtimeSession.UserId,
                        currentUser.CloudTenantId,
                        turnId);
                    if (ct.IsCancellationRequested)
                    {
                        yield break;
                    }

                    var errorChunk = CreateSessionStateErrorChunk(
                        failure,
                        nameof(ChatStreamHandler));
                    assistantRenderChunks.Add(errorChunk);
                    yield return errorChunk;
                }
                else
                {
                    var metadataChunk = new ChatChunk(
                        "HarnessAgent",
                        ChunkType.AgentEvent,
                        JsonSerializer.Serialize(new
                        {
                            stage = "agent_session_state",
                            detail = "Harness AgentSession state persisted.",
                            recoverable = true,
                            metadata = new Dictionary<string, string>(),
                            sessionId = request.SessionId,
                            mode = completedMode == RuntimeAgentMode.Execute
                                ? "execute"
                                : "plan",
                            status = completedState!.Status.ToString(),
                            version = completedState.Version,
                            pendingApproval = completedState.PendingApprovals.Count > 0
                        }));
                    assistantRenderChunks.Add(metadataChunk);
                    yield return metadataChunk;
                }
            }
        }

        await PersistMessagesAsync(
            request.SessionId,
            assistantText,
            assistantRenderChunks,
            pendingMessages,
            ct);
    }

    private async Task<(SessionRuntimeSnapshot? Value, ChatChunk? Error)> LoadSessionSafelyAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await chatStreamRuntime.LoadSessionAsync(
                sessionRepository,
                sessionId,
                cancellationToken);
            return session is null
                ? (null, AgentStreamRuntime.CreateErrorChunk(
                    "session_not_found",
                    "未找到对应的会话。",
                    nameof(ChatStreamHandler),
                    "当前会话不存在或已被删除，请刷新后重试。"))
                : (session, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (null, null);
        }
        catch (Exception exception)
        {
            return (null, AgentStreamRuntime.CreateErrorChunk(
                exception,
                nameof(ChatStreamHandler),
                AppProblemCodes.ChatStreamFailed,
                "对话执行失败，请稍后重试。"));
        }
    }

    private static void CaptureApprovalBindings(
        RuntimeAgentUpdate update,
        IReadOnlyCollection<AiToolDefinition> tools,
        SessionRuntimeSnapshot session,
        string? tenantId,
        ICollection<AgentApprovalBinding> approvals)
    {
        foreach (var approval in update.Contents.OfType<AiToolApprovalRequestContent>())
        {
            var call = approval.Request.ToolCall;
            var definition = tools.SingleOrDefault(
                tool => string.Equals(tool.Name, call.Name, StringComparison.Ordinal));
            if (definition is null || !definition.RequiresApproval)
            {
                throw new InvalidOperationException(
                    "Harness surfaced an approval for an unknown or non-approval tool.");
            }

            approvals.Add(new AgentApprovalBinding(
                session.Id,
                session.UserId,
                string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(),
                approval.Request.RequestId,
                call.CallId,
                call.Name,
                call.Kind,
                call.ServerName,
                call.TargetType,
                call.TargetName,
                call.ToolName,
                call.Arguments,
                definition.SchemaVersion,
                CanonicalJson.ComputeSha256(CanonicalJson.Serialize(call.Arguments))));
        }
    }

    private async Task TryInterruptAsync(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        Guid turnId)
    {
        try
        {
            await agentSessionStateStore.InterruptTurnAsync(
                sessionId,
                userId,
                tenantId,
                turnId,
                CancellationToken.None);
        }
        catch (AgentSessionStateException)
        {
            // Preserve the first terminal state; never replay or rewrite it.
        }
    }

    private async Task PersistMessagesAsync(
        Guid sessionId,
        StringBuilder assistantText,
        IReadOnlyCollection<ChatChunk> assistantRenderChunks,
        ICollection<SessionMessageAppend> pendingMessages,
        CancellationToken cancellationToken)
    {
        if (assistantText.Length > 0 || assistantRenderChunks.Count > 0)
        {
            pendingMessages.Add(new SessionMessageAppend(
                assistantText.Length > 0 ? assistantText.ToString() : null,
                MessageType.Assistant,
                executionMetadataAccessor.ToMessageSnapshot(),
                assistantRenderChunks));
        }

        if (pendingMessages.Count > 0)
        {
            await messagePersistenceService.AppendBatchAsync(
                sessionId,
                pendingMessages,
                cancellationToken);
        }
    }

    internal static ChatChunk CreateSessionStateErrorChunk(
        Exception exception,
        string source)
    {
        if (exception is JsonException or NotSupportedException)
        {
            return AgentStreamRuntime.CreateErrorChunk(
                AppProblemCodes.AgentSessionResetRequired,
                "Persisted AgentSession state cannot be deserialized safely.",
                source,
                "当前会话需要重建后才能继续。");
        }

        if (exception is not AgentSessionStateException stateException)
        {
            return AgentStreamRuntime.CreateErrorChunk(
                exception,
                source,
                AppProblemCodes.ChatStreamFailed,
                "对话执行失败，请稍后重试。");
        }

        return stateException.Failure switch
        {
            AgentSessionStateFailure.Missing or
                AgentSessionStateFailure.SchemaMismatch or
                AgentSessionStateFailure.Corrupt or
                AgentSessionStateFailure.Oversize or
                AgentSessionStateFailure.Expired =>
                AgentStreamRuntime.CreateErrorChunk(
                    AppProblemCodes.AgentSessionResetRequired,
                    "Persisted AgentSession state cannot be restored safely.",
                    source,
                    "当前会话需要重建后才能继续。"),
            AgentSessionStateFailure.Interrupted =>
                AgentStreamRuntime.CreateErrorChunk(
                    AppProblemCodes.AgentSessionInterrupted,
                    "The previous agent turn was interrupted and will not be replayed.",
                    source,
                    "上一次执行已中断；系统不会自动重放，请新建会话后继续。"),
            AgentSessionStateFailure.ApprovalPending =>
                AgentStreamRuntime.CreateErrorChunk(
                    AppProblemCodes.ApprovalPending,
                    "The AgentSession has a pending approval.",
                    source,
                    "当前会话已有待处理审批，请先完成审批。"),
            AgentSessionStateFailure.VersionConflict or
                AgentSessionStateFailure.AlreadyRunning =>
                AgentStreamRuntime.CreateErrorChunk(
                    AppProblemCodes.AgentSessionVersionConflict,
                    "The AgentSession version changed or is active.",
                    source,
                    "会话状态已变化，请刷新后重试。"),
            _ => AgentStreamRuntime.CreateErrorChunk(
                AppProblemCodes.ChatStreamFailed,
                "The AgentSession transition was rejected.",
                source,
                "对话状态校验失败，请刷新后重试。")
        };
    }
}
