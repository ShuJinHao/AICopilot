using System.Runtime.CompilerServices;
using System.Text;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.AiGatewayService.Agents;

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
