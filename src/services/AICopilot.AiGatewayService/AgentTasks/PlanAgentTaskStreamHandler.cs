using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Serialization;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.AiGatewayService.AgentTasks;

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
    : IStreamRequestHandler<PlanAgentTaskCommand, ChatChunk>
{
    private const string Source = nameof(PlanAgentTaskStreamHandler);

    public async IAsyncEnumerable<ChatChunk> Handle(
        PlanAgentTaskCommand request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var responseBuffers = AgentStreamRuntime.CreateResponseBuffers();
        var assistantText = responseBuffers.AssistantText;
        var assistantRenderChunks = responseBuffers.AssistantRenderChunks;
        var pendingMessages = responseBuffers.PendingMessages;
        SessionRuntimeSnapshot? session = null;
        ChatChunk? earlyErrorChunk = null;

        try
        {
            session = await chatStreamRuntime.LoadSessionAsync(sessionRepository, request.SessionId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            yield break;
        }
        catch (Exception exception)
        {
            earlyErrorChunk = AgentStreamRuntime.CreateErrorChunk(
                exception,
                Source,
                AppProblemCodes.ChatStreamFailed,
                "计划草案生成失败，请稍后重试。");
        }

        if (earlyErrorChunk is not null)
        {
            yield return earlyErrorChunk;
            yield break;
        }

        if (session == null)
        {
            yield return AgentStreamRuntime.CreateErrorChunk(
                "session_not_found",
                "未找到对应的会话。",
                Source,
                "当前会话不存在或已被删除，请刷新后重试。");
            yield break;
        }

        if (currentUser.Id != session.UserId)
        {
            yield return AgentStreamRuntime.CreateErrorChunk(
                AuthProblemCodes.MissingPermission,
                "当前用户无权操作该会话。",
                Source,
                "当前账号无权操作该会话。");
            yield break;
        }

        IAsyncDisposable? sessionLock = null;
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
            earlyErrorChunk = AgentStreamRuntime.CreateErrorChunk(
                exception,
                Source,
                AppProblemCodes.ChatStreamFailed,
                "计划草案生成失败，请稍后重试。");
        }

        if (earlyErrorChunk is not null)
        {
            yield return earlyErrorChunk;
            yield break;
        }

        if (sessionLock is null)
        {
            yield break;
        }

        Exception? failure = null;
        await using (sessionLock)
        {
            await using var enumerator = HandleCore(request, assistantText, pendingMessages, ct).GetAsyncEnumerator(ct);
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
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
            var failureEventChunk = CreateFailureEventChunk(failure);
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

        if (pendingMessages.Count > 0)
        {
            Exception? appendFailure = null;
            try
            {
                await messagePersistenceService.AppendBatchAsync(request.SessionId, pendingMessages, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                yield break;
            }
            catch (Exception exception)
            {
                appendFailure = exception;
            }

            if (appendFailure is not null)
            {
                yield return AgentStreamRuntime.CreateErrorChunk(
                    appendFailure,
                    Source,
                    AppProblemCodes.ChatStreamFailed,
                    "计划消息保存失败，请刷新后重试。");
            }
        }
    }

    private async IAsyncEnumerable<ChatChunk> HandleCore(
        PlanAgentTaskCommand request,
        StringBuilder assistantText,
        ICollection<SessionMessageAppend> pendingMessages,
        [EnumeratorCancellation] CancellationToken ct)
    {
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
            detail: "Discovering allowed capabilities and tool descriptions without execution.");

        var result = await sender.Send(request, ct);
        if (!result.IsSuccess || result.Value is null)
        {
            yield return CreateFailureEventChunk(result);
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
                ["planKind"] = "PlanDraft",
                ["schemaVersion"] = result.Value.PlanSchemaVersion ?? string.Empty,
                ["planDigest"] = result.Value.PlanDigest ?? string.Empty,
                ["topologyProfile"] = result.Value.TopologyProfile ?? string.Empty,
                ["isExecutable"] = result.Value.IsPlanExecutable.ToString().ToLowerInvariant()
            });
        yield return CreateTextChunk(assistantText, summary);
        yield return new ChatChunk(Source, ChunkType.AgentTask, result.Value.ToJson());
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

    internal static ChatChunk CreateProblemChunk(StringBuilder assistantText, Result<AgentTaskDto> result)
    {
        var failure = ResolvePlanDraftFailure(result);
        return AgentStreamRuntime.CreateErrorChunk(
            assistantText,
            failure.Code,
            failure.Detail,
            Source,
            failure.UserFacingMessage);
    }

    internal static ChatChunk CreateFailureEventChunk(Result<AgentTaskDto> result)
    {
        var failure = ResolvePlanDraftFailure(result);
        return CreateAgentEventChunk(
            "plan_draft_failed",
            failure.Detail,
            recoverable: true,
            code: failure.Code,
            suggestedAction: "Adjust the goal or model configuration, then retry PlanDraft generation.");
    }

    internal static ChatChunk CreateFailureEventChunk(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var planFailure = AgentStreamRuntime.ResolvePlanPersistenceDisclosure(exception);
        return CreateAgentEventChunk(
            "plan_draft_failed",
            planFailure?.Detail ?? "PlanDraft generation failed before a valid draft could be produced.",
            recoverable: true,
            code: planFailure?.Code ?? AppProblemCodes.ChatStreamFailed,
            suggestedAction: "Retry plan draft generation after checking model and session state.");
    }

    private static PlanDraftFailureProjection ResolvePlanDraftFailure(Result<AgentTaskDto> result)
    {
        var errors = result.Errors?.ToArray();
        var publicPlanFailure = AgentPlanPublicFailureDisclosurePolicy.ResolveResultErrors(errors);
        if (publicPlanFailure is not null)
        {
            return new PlanDraftFailureProjection(
                publicPlanFailure.Disclosure.Code,
                publicPlanFailure.Disclosure.Detail,
                publicPlanFailure.Disclosure.UserFacingMessage);
        }

        var problem = errors?.OfType<ApiProblemDescriptor>().FirstOrDefault();
        if (problem is null)
        {
            var defaultFailure = AgentPlanPublicFailureDisclosurePolicy.Resolve(
                AppProblemCodes.AgentPlanInvalid)!;
            return new PlanDraftFailureProjection(
                defaultFailure.Code,
                defaultFailure.Detail,
                defaultFailure.UserFacingMessage);
        }

        return new PlanDraftFailureProjection(problem.Code, problem.Detail, problem.Detail);
    }

    private static string BuildPlanSummary(AgentTaskDto task)
    {
        var stepLine = task.Steps.Count == 0
            ? "当前目标暂未形成可执行步骤，确认执行前会继续进行能力校验。"
            : $"计划包含 {task.Steps.Count} 个步骤，确认前不会启动 Worker。";
        return $"我已生成计划草案：{task.Title}\n{stepLine}\n";
    }

    private sealed record PlanAgentTaskStreamEvent(
        [property: JsonPropertyName("stage")] string Stage,
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("detail")] string Detail,
        [property: JsonPropertyName("recoverable")] bool Recoverable,
        [property: JsonPropertyName("suggestedAction")] string? SuggestedAction,
        [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string> Metadata);

    private sealed record PlanDraftFailureProjection(
        string Code,
        string Detail,
        string UserFacingMessage);
}
