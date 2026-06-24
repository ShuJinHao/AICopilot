using System.Runtime.CompilerServices;
using System.Text;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.AiGatewayService.Agents;

public class ApprovalDecisionStreamHandler(
    IReadRepository<Session> sessionRepository,
    ICurrentUser currentUser,
    AgentWorkflowPipeline workflowPipeline,
    SessionMessagePersistenceService messagePersistenceService,
    IFinalAgentContextStore finalAgentContextStore,
    IFinalAgentContextSerializer finalAgentContextSerializer,
    ApprovalRequirementResolver approvalRequirementResolver,
    ISessionExecutionLock sessionExecutionLock,
    IAgentExecutionMetadataAccessor executionMetadataAccessor,
    IAgentStreamRuntime chatStreamRuntime)
    : IStreamRequestHandler<ApprovalDecisionStreamRequest, ChatChunk>
{
    public async IAsyncEnumerable<ChatChunk> Handle(
        ApprovalDecisionStreamRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var assistantText = new StringBuilder();
        var assistantRenderChunks = new List<ChatChunk>();
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

                assistantRenderChunks.Add(enumerator.Current);
                yield return enumerator.Current;
            }
        }

        if (failure != null)
        {
            var errorChunk = AgentStreamRuntime.CreateErrorChunk(
                assistantText,
                failure,
                nameof(ApprovalDecisionStreamHandler),
                AppProblemCodes.ApprovalStreamFailed,
                "审批处理失败，请稍后重试。");
            assistantRenderChunks.Add(errorChunk);
            yield return errorChunk;
        }

        if (assistantText.Length > 0 || assistantRenderChunks.Count > 0)
        {
            pendingMessages.Add(new SessionMessageAppend(
                assistantText.Length > 0 ? assistantText.ToString() : null,
                MessageType.Assistant,
                executionMetadataAccessor.ToMessageSnapshot(),
                assistantRenderChunks));
        }

        await messagePersistenceService.AppendBatchAsync(request.SessionId, pendingMessages, ct);
    }

    private async IAsyncEnumerable<ChatChunk> HandleCore(
        ApprovalDecisionStreamRequest request,
        StringBuilder assistantText,
        ICollection<SessionMessageAppend> pendingMessages,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var session = await chatStreamRuntime.LoadSessionAsync(sessionRepository, request.SessionId, ct);
        if (session == null)
        {
            yield return AgentStreamRuntime.CreateErrorChunk(
                assistantText,
                "session_not_found",
                "未找到对应的会话。",
                nameof(ApprovalDecisionStreamHandler),
                "当前会话不存在或已被删除，请刷新后重试。");
            yield break;
        }

        if (currentUser.Id != session.UserId)
        {
            yield return AgentStreamRuntime.CreateErrorChunk(
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
            yield return AgentStreamRuntime.CreateErrorChunk(
                assistantText,
                AppProblemCodes.ApprovalAlreadyProcessed,
                "审批上下文已过期，请重新发起本次诊断或建议请求。",
                nameof(ApprovalDecisionStreamHandler),
                "审批上下文已过期，请重新发起本次诊断或建议请求。");
            yield break;
        }

        executionMetadataAccessor.Apply(storedContext.ExecutionMetadata);

        var storedApproval = storedContext.PendingApprovals
            .FirstOrDefault(item => string.Equals(item.CallId, request.CallId, StringComparison.Ordinal));
        if (storedApproval == null)
        {
            yield return AgentStreamRuntime.CreateErrorChunk(
                assistantText,
                AppProblemCodes.ApprovalAlreadyProcessed,
                "该审批请求已处理或已失效。",
                nameof(ApprovalDecisionStreamHandler),
                "该审批请求已处理或已失效，请重新发起新的诊断请求。");
            yield break;
        }

        var validation = await ApprovalDecisionValidator.ValidateAsync(
            request,
            storedApproval,
            session,
            approvalRequirementResolver,
            assistantText,
            ct);
        if (!validation.IsValid)
        {
            yield return validation.Error!;
            yield break;
        }

        pendingMessages.Add(new SessionMessageAppend(
            AgentStreamRuntime.BuildApprovalSummary(
                validation.Identity is null
                    ? validation.ToolName
                    : $"{validation.Identity.TargetName}/{validation.Identity.ToolName}",
                validation.IsApproved,
                request.OnsiteConfirmed,
                validation.Requirement.RequiresOnsiteAttestation),
            MessageType.User));

        await using var agentContext = await finalAgentContextSerializer.RestoreAsync(storedContext, ct);
        agentContext.ApprovalDecisions.Add(new FunctionApprovalDecision(
            request.CallId,
            validation.IsApproved,
            request.OnsiteConfirmed));

        await foreach (var chatChunk in workflowPipeline.ResumeFinalAgentAsync(
                           agentContext,
                           session,
                           assistantText,
                           ct))
        {
            yield return chatChunk;
        }
    }
}
