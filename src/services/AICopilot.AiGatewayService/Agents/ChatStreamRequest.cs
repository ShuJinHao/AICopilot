using System.Runtime.CompilerServices;
using System.Text;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.AiGatewayService.Agents;

[AuthorizeRequirement("AiGateway.Chat")]
public record ChatStreamRequest(Guid SessionId, string Message) : IStreamRequest<ChatChunk>;

[AuthorizeRequirement("AiGateway.Chat")]
public record ApprovalDecisionStreamRequest(
    Guid SessionId,
    string CallId,
    string Decision,
    bool OnsiteConfirmed,
    string? TargetType = null,
    string? TargetName = null,
    string? ToolName = null) : IStreamRequest<ChatChunk>;

public class ChatStreamHandler(
    IReadRepository<Session> sessionRepository,
    ICurrentUser currentUser,
    ChatWorkflowOrchestrator workflowOrchestrator,
    SessionMessagePersistenceService messagePersistenceService,
    IOperationalBoundaryPolicy operationalBoundaryPolicy,
    IManufacturingSceneClassifier sceneClassifier,
    IFinalAgentContextStore finalAgentContextStore,
    ISessionExecutionLock sessionExecutionLock)
    : IStreamRequestHandler<ChatStreamRequest, ChatChunk>
{
    public async IAsyncEnumerable<ChatChunk> Handle(
        ChatStreamRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var assistantText = new StringBuilder();
        var pendingMessages = new List<SessionMessageAppend>();
        Exception? failure = null;
        IAsyncDisposable? sessionLock = null;

        try
        {
            sessionLock = await sessionExecutionLock.AcquireAsync(request.SessionId, ct);
        }
        catch (OperationCanceledException)
        {
            yield break;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (sessionLock is not null)
        {
            await using var acquiredLock = sessionLock;
            await using var enumerator = HandleCore(request, assistantText, pendingMessages, ct).GetAsyncEnumerator(ct);

            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    yield break;
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

                yield return enumerator.Current;
            }
        }

        if (failure != null)
        {
            yield return ChatStreamRuntime.CreateErrorChunk(
                assistantText,
                failure,
                nameof(ChatStreamHandler),
                AppProblemCodes.ChatStreamFailed,
                "对话执行失败，请稍后重试。");
        }

        if (assistantText.Length > 0)
        {
            pendingMessages.Add(new SessionMessageAppend(assistantText.ToString(), MessageType.Assistant));
        }

        await messagePersistenceService.AppendBatchAsync(request.SessionId, pendingMessages, ct);
    }

    private async IAsyncEnumerable<ChatChunk> HandleCore(
        ChatStreamRequest request,
        StringBuilder assistantText,
        ICollection<SessionMessageAppend> pendingMessages,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var session = await ChatStreamRuntime.LoadSessionAsync(sessionRepository, request.SessionId, ct);
        if (session == null)
        {
            yield return ChatStreamRuntime.CreateErrorChunk(
                "session_not_found",
                "未找到对应的会话。",
                nameof(ChatStreamHandler),
                "当前会话不存在或已被删除，请刷新后重试。");
            yield break;
        }

        if (currentUser.Id != session.UserId)
        {
            yield return ChatStreamRuntime.CreateErrorChunk(
                AuthProblemCodes.MissingPermission,
                "当前用户无权操作该会话。",
                nameof(ChatStreamHandler),
                "当前账号无权操作该会话。");
            yield break;
        }

        var storedContext = await finalAgentContextStore.GetAsync(request.SessionId, ct);
        if (storedContext?.PendingApprovals.Count > 0)
        {
            yield return ChatStreamRuntime.CreateErrorChunk(
                AppProblemCodes.ApprovalPending,
                "当前会话已有待处理审批，请先处理审批请求。",
                nameof(ChatStreamHandler),
                "当前会话已有待处理审批，请先处理审批请求。");
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            pendingMessages.Add(new SessionMessageAppend(request.Message, MessageType.User));
        }

        var sceneDecision = sceneClassifier.Classify(request.Message);
        var blockedByPolicy = operationalBoundaryPolicy.TryBlockControlRequest(request.Message, out var policyDecision);
        OperationalBoundaryDecision? boundaryDecision = policyDecision;

        if (sceneDecision.Scene == ManufacturingSceneType.ControlBlocked || blockedByPolicy)
        {
            boundaryDecision ??= new OperationalBoundaryDecision(
                AppProblemCodes.ControlActionBlocked,
                "AICopilot 只提供观察、诊断、建议和知识问答，不执行任何控制动作。",
                "我不能直接执行重启、写参数、下发配方、写入 PLC 或状态切换。如果需要，我可以继续给出诊断结论、风险提示和人工执行前检查项。");
            assistantText.Append(boundaryDecision.UserFacingMessage);
            yield return ChatStreamRuntime.CreateErrorChunk(
                boundaryDecision.Code,
                boundaryDecision.Detail,
                "OperationalBoundaryPolicy",
                boundaryDecision.UserFacingMessage);
            yield break;
        }

        await foreach (var chatChunk in workflowOrchestrator.RunIntentWorkflowAsync(
                           request,
                           session,
                           assistantText,
                           ct))
        {
            yield return chatChunk;
        }
    }
}

public class ApprovalDecisionStreamHandler(
    IReadRepository<Session> sessionRepository,
    ICurrentUser currentUser,
    ChatWorkflowOrchestrator workflowOrchestrator,
    SessionMessagePersistenceService messagePersistenceService,
    IFinalAgentContextStore finalAgentContextStore,
    IFinalAgentContextSerializer finalAgentContextSerializer,
    ApprovalRequirementResolver approvalRequirementResolver,
    ISessionExecutionLock sessionExecutionLock)
    : IStreamRequestHandler<ApprovalDecisionStreamRequest, ChatChunk>
{
    public async IAsyncEnumerable<ChatChunk> Handle(
        ApprovalDecisionStreamRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var assistantText = new StringBuilder();
        var pendingMessages = new List<SessionMessageAppend>();
        Exception? failure = null;
        IAsyncDisposable? sessionLock = null;

        try
        {
            sessionLock = await sessionExecutionLock.AcquireAsync(request.SessionId, ct);
        }
        catch (OperationCanceledException)
        {
            yield break;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (sessionLock is not null)
        {
            await using var acquiredLock = sessionLock;
            await using var enumerator = HandleCore(request, assistantText, pendingMessages, ct).GetAsyncEnumerator(ct);
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    yield break;
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

                yield return enumerator.Current;
            }
        }

        if (failure != null)
        {
            yield return ChatStreamRuntime.CreateErrorChunk(
                assistantText,
                failure,
                nameof(ApprovalDecisionStreamHandler),
                AppProblemCodes.ApprovalStreamFailed,
                "审批处理失败，请稍后重试。");
        }

        if (assistantText.Length > 0)
        {
            pendingMessages.Add(new SessionMessageAppend(assistantText.ToString(), MessageType.Assistant));
        }

        await messagePersistenceService.AppendBatchAsync(request.SessionId, pendingMessages, ct);
    }

    private async IAsyncEnumerable<ChatChunk> HandleCore(
        ApprovalDecisionStreamRequest request,
        StringBuilder assistantText,
        ICollection<SessionMessageAppend> pendingMessages,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var session = await ChatStreamRuntime.LoadSessionAsync(sessionRepository, request.SessionId, ct);
        if (session == null)
        {
            yield return ChatStreamRuntime.CreateErrorChunk(
                assistantText,
                "session_not_found",
                "未找到对应的会话。",
                nameof(ApprovalDecisionStreamHandler),
                "当前会话不存在或已被删除，请刷新后重试。");
            yield break;
        }

        if (currentUser.Id != session.UserId)
        {
            yield return ChatStreamRuntime.CreateErrorChunk(
                assistantText,
                AuthProblemCodes.MissingPermission,
                "当前用户无权操作该会话。",
                nameof(ApprovalDecisionStreamHandler),
                "当前账号无权操作该会话。");
            yield break;
        }

        var storedContext = await finalAgentContextStore.GetAsync(request.SessionId, ct);
        if (storedContext == null)
        {
            yield return ChatStreamRuntime.CreateErrorChunk(
                assistantText,
                AppProblemCodes.ApprovalAlreadyProcessed,
                "审批上下文已过期，请重新发起本次诊断或建议请求。",
                nameof(ApprovalDecisionStreamHandler),
                "审批上下文已过期，请重新发起本次诊断或建议请求。");
            yield break;
        }

        var storedApproval = storedContext.PendingApprovals
            .FirstOrDefault(item => string.Equals(item.CallId, request.CallId, StringComparison.Ordinal));
        if (storedApproval == null)
        {
            yield return ChatStreamRuntime.CreateErrorChunk(
                assistantText,
                AppProblemCodes.ApprovalAlreadyProcessed,
                "该审批请求已处理或已失效。",
                nameof(ApprovalDecisionStreamHandler),
                "该审批请求已处理或已失效，请重新发起新的诊断请求。");
            yield break;
        }

        bool isApproved;
        var decision = request.Decision.Trim();
        if (string.Equals(decision, "approved", StringComparison.OrdinalIgnoreCase))
        {
            isApproved = true;
        }
        else if (string.Equals(decision, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            isApproved = false;
        }
        else
        {
            yield return ChatStreamRuntime.CreateErrorChunk(
                assistantText,
                "invalid_approval_decision",
                "审批决策只能是 approved 或 rejected。",
                nameof(ApprovalDecisionStreamHandler),
                "审批决策无效，请重新选择批准或拒绝。");
            yield break;
        }

        if (!ApprovalIdentityMatches(request, storedApproval))
        {
            yield return ChatStreamRuntime.CreateErrorChunk(
                assistantText,
                AppProblemCodes.ApprovalAlreadyProcessed,
                "审批请求的工具身份与待审批上下文不一致。",
                nameof(ApprovalDecisionStreamHandler),
                "审批请求已失效，请重新发起新的诊断请求。");
            yield break;
        }

        var identity = BuildStoredIdentity(storedApproval);
        var toolName = identity?.ToolName ?? storedApproval.ToolName ?? storedApproval.CallId;
        var requirement = await approvalRequirementResolver.GetMergedRequirementByIdentityAsync(identity, ct);
        var nowUtc = DateTimeOffset.UtcNow;

        if (isApproved && requirement.RequiresOnsiteAttestation)
        {
            if (!session.OnsiteConfirmationExpiresAt.HasValue || !session.OnsiteConfirmedAt.HasValue)
            {
                yield return ChatStreamRuntime.CreateErrorChunk(
                    assistantText,
                    AppProblemCodes.OnsitePresenceRequired,
                    "该工具能力要求先完成会话级人工在岗声明。",
                    nameof(ApprovalDecisionStreamHandler),
                    "该建议需要先确认现场有人在岗，请先设置在岗声明。");
                yield break;
            }

            if (!session.HasValidOnsiteAttestation(nowUtc))
            {
                yield return ChatStreamRuntime.CreateErrorChunk(
                    assistantText,
                    AppProblemCodes.OnsitePresenceExpired,
                    "当前会话的在岗声明已过期，请重新确认人工在场状态。",
                    nameof(ApprovalDecisionStreamHandler),
                    "当前会话的在岗声明已过期，请重新确认现场有人在岗。");
                yield break;
            }

            if (!request.OnsiteConfirmed)
            {
                yield return ChatStreamRuntime.CreateErrorChunk(
                    assistantText,
                    AppProblemCodes.ApprovalReconfirmationRequired,
                    "审批前必须再次显式确认现场有人在岗。",
                    nameof(ApprovalDecisionStreamHandler),
                    "批准前需要再次确认现场有人在岗。");
                yield break;
            }
        }

        pendingMessages.Add(new SessionMessageAppend(
            ChatStreamRuntime.BuildApprovalSummary(
                identity is null ? toolName : $"{identity.TargetName}/{identity.ToolName}",
                isApproved,
                request.OnsiteConfirmed,
                requirement.RequiresOnsiteAttestation),
            MessageType.User));

        await using var agentContext = await finalAgentContextSerializer.RestoreAsync(storedContext, ct);
        agentContext.ApprovalDecisions.Add(new FunctionApprovalDecision(request.CallId, isApproved, request.OnsiteConfirmed));

        await foreach (var chatChunk in workflowOrchestrator.ResumeFinalAgentAsync(
                           agentContext,
                           session,
                           assistantText,
                           ct))
        {
            yield return chatChunk;
        }
    }

    private static bool ApprovalIdentityMatches(
        ApprovalDecisionStreamRequest request,
        StoredToolApprovalRequest storedApproval)
    {
        if (string.IsNullOrWhiteSpace(request.TargetType)
            && string.IsNullOrWhiteSpace(request.TargetName)
            && string.IsNullOrWhiteSpace(request.ToolName))
        {
            return true;
        }

        return string.Equals(request.TargetType, storedApproval.TargetType, StringComparison.OrdinalIgnoreCase)
               && string.Equals(request.TargetName, storedApproval.TargetName, StringComparison.OrdinalIgnoreCase)
               && string.Equals(request.ToolName, storedApproval.ToolName, StringComparison.OrdinalIgnoreCase);
    }

    private static AiToolIdentity? BuildStoredIdentity(StoredToolApprovalRequest storedApproval)
    {
        if (!Enum.TryParse<AiToolTargetType>(storedApproval.TargetType, ignoreCase: true, out var targetType)
            || string.IsNullOrWhiteSpace(storedApproval.TargetName)
            || string.IsNullOrWhiteSpace(storedApproval.ToolName))
        {
            return null;
        }

        var kind = Enum.TryParse<AiToolCallKind>(storedApproval.ToolKind, ignoreCase: true, out var parsedKind)
            ? parsedKind
            : targetType == AiToolTargetType.McpServer
                ? AiToolCallKind.Mcp
                : AiToolCallKind.Function;

        return new AiToolIdentity(kind, targetType, storedApproval.TargetName, storedApproval.ToolName);
    }
}
