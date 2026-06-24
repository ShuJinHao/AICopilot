using System.Runtime.CompilerServices;
using System.Text;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.CrossCutting.Serialization;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.AiGatewayService.AgentTasks;

[AuthorizeRequirement("AiGateway.PlanAgentTask")]
public sealed record PlanAgentTaskStreamRequest(
    Guid SessionId,
    string Goal,
    AgentTaskType TaskType,
    Guid? ModelId,
    IReadOnlyCollection<Guid>? UploadIds = null,
    IReadOnlyCollection<Guid>? KnowledgeBaseIds = null,
    IReadOnlyCollection<Guid>? DataSourceIds = null,
    IReadOnlyCollection<string>? BusinessDomains = null,
    string? QueryMode = null,
    bool RequiresDataApproval = false,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    string? PlannerMode = null,
    bool ForceStaticPlanner = false,
    string? SkillCode = null,
    IReadOnlyCollection<string>? PreferredToolCodes = null) : IStreamRequest<ChatChunk>;

public sealed class PlanAgentTaskStreamHandler(
    IReadRepository<Session> sessionRepository,
    ICurrentUser currentUser,
    ISender sender,
    SessionMessagePersistenceService messagePersistenceService,
    IOperationalBoundaryPolicy operationalBoundaryPolicy,
    IManufacturingSceneClassifier sceneClassifier,
    IFinalAgentContextStore finalAgentContextStore,
    ISessionExecutionLock sessionExecutionLock,
    IAgentExecutionMetadataAccessor executionMetadataAccessor,
    IAgentStreamRuntime chatStreamRuntime)
    : IStreamRequestHandler<PlanAgentTaskStreamRequest, ChatChunk>
{
    private const string Source = nameof(PlanAgentTaskStreamHandler);

    public async IAsyncEnumerable<ChatChunk> Handle(
        PlanAgentTaskStreamRequest request,
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
            var failureEventChunk = CreateAgentEventChunk(
                "plan_draft_failed",
                failure.Message,
                recoverable: true,
                code: AppProblemCodes.ChatStreamFailed,
                suggestedAction: "Retry plan draft generation after checking model and session state.");
            assistantRenderChunks.Add(failureEventChunk);
            yield return failureEventChunk;

            var errorChunk = AgentStreamRuntime.CreateErrorChunk(
                assistantText,
                failure,
                Source,
                AppProblemCodes.ChatStreamFailed,
                "计划草案生成失败，请稍后重试。");
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
        PlanAgentTaskStreamRequest request,
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
                Source,
                "当前会话不存在或已被删除，请刷新后重试。");
            yield break;
        }

        if (currentUser.Id != session.UserId)
        {
            yield return AgentStreamRuntime.CreateErrorChunk(
                assistantText,
                AuthProblemCodes.MissingPermission,
                "当前用户无权操作该会话。",
                Source,
                "当前账号无权操作该会话。");
            yield break;
        }

        var storedContext = await finalAgentContextStore.GetAsync(request.SessionId, ct);
        if (storedContext?.PendingApprovals.Count > 0)
        {
            yield return AgentStreamRuntime.CreateErrorChunk(
                assistantText,
                AppProblemCodes.ApprovalPending,
                "当前会话已有待处理审批，请先处理审批请求。",
                Source,
                "当前会话已有待处理审批，请先处理审批请求。");
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(request.Goal))
        {
            pendingMessages.Add(new SessionMessageAppend(request.Goal, MessageType.User));
        }

        var sceneDecision = sceneClassifier.Classify(request.Goal);
        var blockedByPolicy = operationalBoundaryPolicy.TryBlockControlRequest(request.Goal, out var policyDecision);
        OperationalBoundaryDecision? boundaryDecision = policyDecision;
        if (sceneDecision.Scene == ManufacturingSceneType.ControlBlocked || blockedByPolicy)
        {
            boundaryDecision ??= new OperationalBoundaryDecision(
                AppProblemCodes.ControlActionBlocked,
                "AICopilot 只提供观察、诊断、建议和知识问答，不执行任何控制动作。",
                "我不能直接执行重启、写参数、下发配方、写入 PLC 或状态切换。如果需要，我可以继续给出诊断结论、风险提示和人工执行前检查项。");
            assistantText.Append(boundaryDecision.UserFacingMessage);
            yield return AgentStreamRuntime.CreateErrorChunk(
                boundaryDecision.Code,
                boundaryDecision.Detail,
                Source,
                boundaryDecision.UserFacingMessage);
            yield break;
        }

        yield return CreateAgentEventChunk(
            "plan_draft_started",
            detail: "PlanDraft generation started.",
            metadata: new Dictionary<string, string>
            {
                ["mode"] = "PlanDraft",
                ["executesCloudQuery"] = "false",
                ["executesMcpTool"] = "false",
                ["queuesWorker"] = "false"
            });
        yield return CreateAgentEventChunk(
            "intent_understanding",
            detail: "Understanding user goal and routing intent.");
        yield return CreateAgentEventChunk(
            "capability_discovery",
            detail: "Discovering allowed skills and tool descriptions without execution.");

        var result = await sender.Send(ToCommand(request), ct);
        if (!result.IsSuccess || result.Value is null)
        {
            var problem = result.Errors?.OfType<ApiProblemDescriptor>().FirstOrDefault();
            yield return CreateAgentEventChunk(
                "plan_draft_failed",
                problem?.Detail
                    ?? result.Errors?.Select(error => error?.ToString())
                        .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
                    ?? "PlanDraft generation failed.",
                recoverable: true,
                code: problem?.Code ?? AppProblemCodes.AgentPlanInvalid,
                suggestedAction: "Adjust the goal or model configuration, then retry PlanDraft generation.");
            yield return CreateProblemChunk(assistantText, result);
            yield break;
        }

        var summary = BuildPlanSummary(result.Value);
        yield return CreateAgentEventChunk(
            "plan_draft_ready",
            detail: "PlanDraft is ready for user confirmation.",
            metadata: new Dictionary<string, string>
            {
                ["taskId"] = result.Value.Id.ToString(),
                ["status"] = result.Value.Status,
                ["planKind"] = "PlanDraft"
            });
        yield return CreateTextChunk(assistantText, summary);
        yield return new ChatChunk(Source, ChunkType.AgentTask, result.Value.ToJson());
    }

    private static PlanAgentTaskCommand ToCommand(PlanAgentTaskStreamRequest request)
    {
        return new PlanAgentTaskCommand(
            request.SessionId,
            request.Goal,
            request.TaskType,
            request.ModelId,
            request.UploadIds,
            request.KnowledgeBaseIds,
            request.DataSourceIds,
            request.BusinessDomains,
            request.QueryMode,
            request.RequiresDataApproval,
            request.ArtifactTypes,
            request.PlannerMode,
            request.ForceStaticPlanner,
            request.SkillCode,
            request.PreferredToolCodes);
    }

    private static ChatChunk CreateTextChunk(StringBuilder assistantText, string content)
    {
        assistantText.Append(content);
        return new ChatChunk(Source, ChunkType.Text, content);
    }

    private static ChatChunk CreateAgentEventChunk(
        string stage,
        string detail,
        bool recoverable = true,
        string? code = null,
        string? suggestedAction = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new ChatChunk(
            Source,
            ChunkType.AgentEvent,
            new PlanAgentTaskStreamEvent(
                stage,
                code,
                detail,
                recoverable,
                suggestedAction,
                metadata ?? new Dictionary<string, string>()).ToJson());
    }

    private static ChatChunk CreateProblemChunk(StringBuilder assistantText, Result<AgentTaskDto> result)
    {
        var problem = result.Errors?.OfType<ApiProblemDescriptor>().FirstOrDefault();
        var detail = problem?.Detail
            ?? result.Errors?.Select(error => error?.ToString())
                .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
            ?? "计划草案生成失败。";
        return AgentStreamRuntime.CreateErrorChunk(
            assistantText,
            problem?.Code ?? AppProblemCodes.AgentPlanInvalid,
            detail,
            Source,
            detail);
    }

    private static string BuildPlanSummary(AgentTaskDto task)
    {
        var stepLine = task.Steps.Count == 0
            ? "当前目标暂未形成可执行步骤，确认执行前会继续进行能力校验。"
            : $"计划包含 {task.Steps.Count} 个步骤，确认前不会启动 Worker。";
        return $"我已生成计划草案：{task.Title}\n{stepLine}\n";
    }

    private sealed record PlanAgentTaskStreamEvent(
        string Stage,
        string? Code,
        string Detail,
        bool Recoverable,
        string? SuggestedAction,
        IReadOnlyDictionary<string, string> Metadata);
}
