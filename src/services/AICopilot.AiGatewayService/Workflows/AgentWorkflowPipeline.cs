using System.Runtime.CompilerServices;
using System.Text;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows;

public sealed record AgentPlanDraftWorkflowResult(
    string Scene,
    IReadOnlyCollection<IntentResult> Intents,
    IReadOnlyCollection<AiToolDefinition> Tools,
    ChatExecutionMetadataSnapshot ExecutionMetadata)
{
    internal AgentIntentRegistrySnapshot RegistrySnapshot { get; init; } = AgentIntentRegistryV1.FrozenSnapshot;
}

public class AgentWorkflowPipeline(
    IntentRoutingExecutor intentRouting,
    KnowledgeRetrievalExecutor knowledgeRetrieval,
    DataAnalysisExecutor dataAnalysis,
    BusinessPolicyExecutor businessPolicy,
    ContextAggregatorExecutor contextAggregator,
    FinalAgentBuildExecutor agentBuild,
    FinalAgentRunExecutor agentRun,
    IFinalAgentContextStore finalAgentContextStore,
    IFinalAgentContextSerializer finalAgentContextSerializer,
    ILogger<AgentWorkflowPipeline> logger,
    IAgentTaskChatEvidenceProvider? taskChatEvidenceProvider = null,
    IBusinessQueryContextStore? businessQueryContextStore = null)
{
    public async Task<AgentPlanDraftWorkflowResult> RunPlanDraftWorkflowAsync(
        ChatStreamRequest request,
        CancellationToken ct = default)
    {
        var routing = await intentRouting.ExecuteAsync(request, ct);
        return new AgentPlanDraftWorkflowResult(
            routing.Scene.ToString(),
            routing.Intents,
            [],
            routing.ExecutionMetadata)
        {
            RegistrySnapshot = routing.RegistrySnapshot
        };
    }

    public async Task<AgentPlanDraftWorkflowResult> RunPlanDraftRoutingOnlyAsync(
        ChatStreamRequest request,
        CancellationToken ct = default)
    {
        var routing = await intentRouting.ExecuteAsync(request, ct);
        return new AgentPlanDraftWorkflowResult(
            routing.Scene.ToString(),
            routing.Intents,
            [],
            routing.ExecutionMetadata)
        {
            RegistrySnapshot = routing.RegistrySnapshot
        };
    }

    public async IAsyncEnumerable<ChatChunk> RunIntentWorkflowAsync(
        ChatStreamRequest request,
        SessionRuntimeSnapshot? session,
        StringBuilder assistantText,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        IntentRoutingStepResult routing;
        if (businessQueryContextStore?.TryConfirmPending(
                request.SessionId,
                request.Message,
                out var confirmedBusinessQuery) == true)
        {
            routing = CreateServerConfirmedRouting(
                confirmedBusinessQuery,
                "server-confirmed-business-query");
        }
        else
        {
            routing = await intentRouting.ExecuteAsync(request, ct);
        }

        var routingMetadataChunk = AgentStreamRuntime.CreateMetadataChunk(
            routing.ExecutionMetadata,
            IntentRoutingExecutor.ExecutorId);
        if (routingMetadataChunk is not null)
        {
            yield return routingMetadataChunk;
        }

        if (!string.IsNullOrWhiteSpace(routing.ResponseText))
        {
            yield return new ChatChunk(IntentRoutingExecutor.ExecutorId, ChunkType.Intent, routing.ResponseText);
        }

        AgentTaskChatEvidenceContext? boundTaskEvidence = null;
        if (request.ReferencedAgentTaskId is { } referencedTaskId)
        {
            var routedOnlyGeneralChat = routing.Intents.Count > 0 &&
                                        routing.Intents.All(intent => string.Equals(
                                            intent.Intent,
                                            "General.Chat",
                                            StringComparison.Ordinal));
            var freshReadOnlyScopeRequested =
                AgentTaskChatEvidenceReusePolicy.RequiresFreshReadOnlyQuery(request.Message);
            if (routedOnlyGeneralChat && freshReadOnlyScopeRequested)
            {
                yield return CreateTaskEvidenceEvent(
                    "task_evidence_refresh_required",
                    "检测到设备、工序、日志级别或时间范围变化，但本轮未形成安全的数据读取意图；系统未复用旧任务证据。",
                    evidenceSetDigest: null,
                    freshQueryRequired: true);
                yield return AgentStreamRuntime.CreateErrorChunk(
                    AppProblemCodes.ChatStreamFailed,
                    "A changed readonly data scope was routed as General.Chat, so the sealed task Evidence was not reused and no query was executed.",
                    nameof(AgentWorkflowPipeline),
                    "你改变了查询范围，但本轮没有形成可安全执行的新只读查询。请明确写出要查询的设备、工序、日志级别和时间范围后重试。旧任务结果未被复用。");
                yield break;
            }

            var canReuseCompletedTaskEvidence = routedOnlyGeneralChat;
            if (canReuseCompletedTaskEvidence)
            {
                if (session is null || taskChatEvidenceProvider is null)
                {
                    yield return AgentStreamRuntime.CreateErrorChunk(
                        AppProblemCodes.AgentNodeRunStateConflict,
                        "Completed AgentTask Evidence binding is unavailable for the current Chat runtime.",
                        nameof(AgentWorkflowPipeline),
                        "当前任务结果无法安全绑定到追问，请刷新任务结果后重试。");
                    yield break;
                }

                var binding = await taskChatEvidenceProvider.BindCompletedTaskAsync(
                    request.SessionId,
                    session.UserId,
                    referencedTaskId,
                    ct);
                if (!binding.IsSuccess)
                {
                    var problem = binding.Errors?.OfType<ApiProblemDescriptor>().FirstOrDefault();
                    yield return AgentStreamRuntime.CreateErrorChunk(
                        problem?.Code ?? AppProblemCodes.AgentNodeRunStateConflict,
                        problem?.Detail ?? "Completed AgentTask Evidence binding failed.",
                        nameof(AgentWorkflowPipeline),
                        "所选任务结果尚未形成当前会话可复用的封存证据，请刷新任务状态或重新执行查询。");
                    yield break;
                }

                boundTaskEvidence = binding.Value!;
                yield return CreateTaskEvidenceEvent(
                    "task_evidence_reused",
                    "已绑定所选已完成任务的封存证据，本轮回答不会重新查询数据。",
                    boundTaskEvidence.EvidenceSetDigest,
                    freshQueryRequired: false);
            }
            else
            {
                yield return CreateTaskEvidenceEvent(
                    "task_evidence_refresh_required",
                    "本轮问题命中新业务或数据范围，将按当前意图重新执行，不复用历史任务证据。",
                    evidenceSetDigest: null,
                    freshQueryRequired: true);
            }
        }

        var sink = new AgentWorkflowSink();
        using var branchCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var branchToken = branchCancellation.Token;
        var branchTasks = AgentWorkflowTopology.ParallelBranches
            .OrderBy(branch => branch.Order)
            .Select(branch =>
            {
                var isRequired = IsBranchRequired(
                    branch.BranchType,
                    routing.Intents,
                    routing.RegistrySnapshot);
                return RunBranchNodeAsync(
                    branch.BranchType,
                    isRequired,
                    () => ExecuteBranchAsync(
                        branch.BranchType,
                        routing.Intents,
                        request.Message,
                        routing.RegistrySnapshot,
                        sink,
                        session,
                        branchToken),
                    request.SessionId,
                    sink,
                    branchToken);
            })
            .ToArray();

        var allBranchesTask = Task.WhenAll(branchTasks);
        _ = CompleteSinkWhenBranchesFinishAsync(allBranchesTask, sink);
        try
        {
            await foreach (var chunk in sink.ReadAllAsync(ct))
            {
                yield return chunk;
            }

            var branchResults = await allBranchesTask;
            var requiredFailureChunk = CreateRequiredBranchFailureChunk(branchResults);
            if (requiredFailureChunk is not null)
            {
                logger.LogWarning(
                    "Agent workflow stopped before final synthesis because required branches failed. Branches={Branches}; FailureCodes={FailureCodes}",
                    string.Join(",", branchResults
                        .Where(result => result.IsRequired && result.Status == BranchExecutionStatus.Failed)
                        .Select(result => result.Type)),
                    string.Join(",", branchResults
                        .Where(result => result.IsRequired && result.Status == BranchExecutionStatus.Failed)
                        .Select(result => result.FailureCode)
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Distinct(StringComparer.Ordinal)));
                yield return requiredFailureChunk;
                yield break;
            }

            if (branchResults
                .Where(result => result.Status == BranchExecutionStatus.Succeeded)
                .SelectMany(result => result.Evidence)
                .Any(evidence => !AgentEvidenceAccessChecker.HasExactScopes(
                    evidence.AllowedConsumerScopes,
                    AgentEvidenceAccessChecker.BuildChatScopes(request.SessionId))))
            {
                yield return AgentStreamRuntime.CreateErrorChunk(
                    AppProblemCodes.ChatStreamFailed,
                    "A workflow Evidence envelope is outside the current session consumer scope.",
                    nameof(AgentWorkflowPipeline),
                    "本轮分析证据不属于当前会话，系统已停止生成最终回答。");
                yield break;
            }

            var generationContext = contextAggregator.Execute(
                request,
                routing.Scene,
                branchResults,
                boundTaskEvidence);

            await using var finalAgentContext = await agentBuild.ExecuteAsync(generationContext, ct);
            await foreach (var chunk in RunFinalAgentAsync(finalAgentContext, session, assistantText, ct))
            {
                yield return chunk;
            }
        }
        finally
        {
            // Async-iterator cancellation and early consumer disposal both pass through here.  The
            // linked token stops cooperative branches, and awaiting Task.WhenAll is the quiescence
            // barrier that prevents a late branch from performing side effects after this method exits.
            branchCancellation.Cancel();
            await ObserveBranchQuiescenceAsync(allBranchesTask).ConfigureAwait(false);
        }
    }

    private static IntentRoutingStepResult CreateServerConfirmedRouting(
        BusinessQueryContext context,
        string routingNote)
    {
        return new IntentRoutingStepResult(
            [
                new IntentResult
                {
                    Intent = context.SemanticPlan!.Intent,
                    Query = context.Question,
                    Confidence = 1,
                    RoutingNote = routingNote,
                    BusinessDataSourceExplicitlySelected = true,
                    ConfirmedBusinessQueryContext = BusinessQueryConfirmation.Complete,
                    ConfirmedBusinessQuery = context
                }
            ],
            ManufacturingSceneType.FallbackToExistingRouting,
            ResponseText: null,
            new ChatExecutionMetadataSnapshot());
    }

    public async IAsyncEnumerable<ChatChunk> ResumeFinalAgentAsync(
        FinalAgentContext agentContext,
        SessionRuntimeSnapshot? session,
        StringBuilder assistantText,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in RunFinalAgentAsync(agentContext, session, assistantText, ct))
        {
            yield return chunk;
        }
    }

    private async IAsyncEnumerable<ChatChunk> RunFinalAgentAsync(
        FinalAgentContext agentContext,
        SessionRuntimeSnapshot? session,
        StringBuilder assistantText,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var compensation = new FinalAgentContextCompensation(
            finalAgentContextStore,
            agentContext.SessionId,
            logger);
        var finalMetadataChunk = AgentStreamRuntime.CreateMetadataChunk(
            agentContext.ExecutionMetadata,
            FinalAgentBuildExecutor.ExecutorId);
        if (finalMetadataChunk is not null)
        {
            yield return finalMetadataChunk;
        }

        await foreach (var chunk in agentRun.ExecuteAsync(agentContext, session, assistantText, ct))
        {
            yield return chunk;
        }

        if (agentContext.FunctionApprovalRequestContents.Count != 0)
        {
            var storedContext = await finalAgentContextSerializer.CreateSnapshotAsync(agentContext, ct);
            await finalAgentContextStore.SetAsync(agentContext.SessionId, storedContext, ct);
            compensation.MarkCompleted();
        }
        else
        {
            await compensation.RemoveAndCompleteAsync();
        }
    }

    private static async Task CompleteSinkWhenBranchesFinishAsync(Task branchTask, AgentWorkflowSink sink)
    {
        try
        {
            await branchTask.ConfigureAwait(false);
            sink.Complete();
        }
        catch (Exception ex)
        {
            sink.Complete(ex);
        }
    }

    private static async Task ObserveBranchQuiescenceAsync(Task branchTask)
    {
        try
        {
            await branchTask.ConfigureAwait(false);
        }
        catch
        {
            // The primary exception/cancellation is already flowing from the iterator body.  This
            // await exists to observe every branch and must never replace that primary outcome.
        }
    }

    internal static async Task<BranchResult> RunBranchSafelyAsync(
        BranchType branchType,
        bool isRequired,
        Func<Task<BranchResult>> execute,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var result = await execute().ConfigureAwait(false);
            if (result.Type != branchType)
            {
                return BranchResult.Failed(
                        branchType,
                        AppProblemCodes.ChatStreamFailed,
                        "Workflow branch returned a mismatched result type.")
                    .WithRequirement(isRequired);
            }

            if (isRequired && result.Status == BranchExecutionStatus.Skipped)
            {
                return BranchResult.Failed(
                        branchType,
                        AppProblemCodes.ChatStreamFailed,
                        "A required workflow branch was skipped unexpectedly.")
                    .WithRequirement(isRequired);
            }

            return result.WithRequirement(isRequired);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Agent workflow branch {BranchType} failed and was marked failed. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                branchType,
                ex.GetType().Name);
            return BranchResult.Failed(
                    branchType,
                    AppProblemCodes.ChatStreamFailed,
                    "Workflow branch execution failed.")
                .WithRequirement(isRequired);
        }
    }

    private async Task<BranchResult> RunBranchNodeAsync(
        BranchType branchType,
        bool isRequired,
        Func<Task<BranchResult>> execute,
        Guid sessionId,
        AgentWorkflowSink sink,
        CancellationToken ct)
    {
        var nodeId = $"chat-{branchType.ToString().ToLowerInvariant()}-branch";
        var nodeKind = branchType switch
        {
            BranchType.BusinessPolicy => "PolicyValidationNode",
            BranchType.Knowledge => "KnowledgeRetrievalNode",
            BranchType.DataAnalysis => "DeterministicComputeNode",
            _ => "DeterministicComputeNode"
        };
        var executionContract = AgentNodeExecutionContract.ForChat(
            nodeId,
            nodeKind,
            isRequired,
            branchType.ToString());
        await WriteNodeEventAsync(
            sink,
            new AgentNodeExecutionEvent(
                AgentNodeExecutionEvent.CurrentSchemaVersion,
                AgentNodeExecutionEventType.Started,
                "Chat",
                nodeId,
                nodeKind,
                branchType.ToString(),
                isRequired,
                EvidenceSetDigest: null,
                FailureCode: null,
                DateTimeOffset.UtcNow),
            ct);

        var result = await RunBranchSafelyAsync(
            branchType,
            isRequired,
            () => AgentNodeExecutionPlane.ExecuteAsync(
                executionContract,
                _ => execute(),
                ct),
            logger,
            ct);
        if (result.Status == BranchExecutionStatus.Succeeded)
        {
            var normalized = AgentWorkflowEvidenceNormalizer.Normalize(result, sessionId);
            if (!normalized.IsSuccess)
            {
                var problem = normalized.Errors?.OfType<ApiProblemDescriptor>().FirstOrDefault();
                result = BranchResult.Failed(
                        branchType,
                        problem?.Code ?? AppProblemCodes.ChatStreamFailed,
                        problem?.Detail ?? "Workflow branch Evidence normalization failed.")
                    .WithRequirement(isRequired);
            }
            else
            {
                result = normalized.Value!.WithRequirement(isRequired);
            }
        }

        var eventType = result.Status switch
        {
            BranchExecutionStatus.Skipped => AgentNodeExecutionEventType.Skipped,
            BranchExecutionStatus.Empty => AgentNodeExecutionEventType.Empty,
            BranchExecutionStatus.Succeeded => AgentNodeExecutionEventType.Succeeded,
            BranchExecutionStatus.Failed => AgentNodeExecutionEventType.Failed,
            _ => AgentNodeExecutionEventType.Failed
        };
        var evidenceSetDigest = result.Evidence.Count == 0
            ? null
            : AgentWorkflowEvidenceNormalizer.ComputeEvidenceSetDigest(result.Evidence);
        await WriteNodeEventAsync(
            sink,
            new AgentNodeExecutionEvent(
                AgentNodeExecutionEvent.CurrentSchemaVersion,
                eventType,
                "Chat",
                nodeId,
                result.Evidence.FirstOrDefault()?.NodeKind ?? nodeKind,
                branchType.ToString(),
                isRequired,
                evidenceSetDigest,
                result.FailureCode,
                DateTimeOffset.UtcNow),
            ct);
        return result;
    }

    private static ValueTask WriteNodeEventAsync(
        AgentWorkflowSink sink,
        AgentNodeExecutionEvent nodeEvent,
        CancellationToken cancellationToken)
    {
        return sink.WriteAsync(
            new ChatChunk(
                nameof(AgentWorkflowPipeline),
                ChunkType.AgentEvent,
                nodeEvent.ToJson()),
            cancellationToken);
    }

    private static ChatChunk CreateTaskEvidenceEvent(
        string stage,
        string detail,
        string? evidenceSetDigest,
        bool freshQueryRequired)
    {
        return new ChatChunk(
            nameof(AgentWorkflowPipeline),
            ChunkType.AgentEvent,
            CanonicalJson.Serialize(new
            {
                stage,
                code = (string?)null,
                detail,
                recoverable = true,
                suggestedAction = (string?)null,
                metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["evidenceSetDigest"] = evidenceSetDigest ?? string.Empty,
                    ["freshQueryRequired"] = freshQueryRequired.ToString().ToLowerInvariant(),
                    ["sourceMode"] = evidenceSetDigest is null
                        ? "FreshReadonlyQuery"
                        : "CompletedAgentTaskEvidence"
                }
            }));
    }

    internal static ChatChunk? CreateRequiredBranchFailureChunk(IEnumerable<BranchResult> branchResults)
    {
        if (!branchResults.Any(result =>
                result.IsRequired
                && result.Status == BranchExecutionStatus.Failed))
        {
            return null;
        }

        return AgentStreamRuntime.CreateErrorChunk(
            AppProblemCodes.ChatStreamFailed,
            "One or more required workflow branches did not complete successfully; final synthesis was not started.",
            nameof(AgentWorkflowPipeline),
            "本次请求所需的只读分析、知识、策略或工具能力未能完成，系统已停止生成最终回答，请稍后重试。");
    }

    private bool IsBranchRequired(
        BranchType branchType,
        IReadOnlyCollection<IntentResult> intents,
        AgentIntentRegistrySnapshot registry)
    {
        return branchType switch
        {
            BranchType.Knowledge => KnowledgeRetrievalExecutor.IsRelevant(intents, registry),
            BranchType.DataAnalysis => DataAnalysisExecutor.IsRelevant(intents, registry),
            BranchType.BusinessPolicy => businessPolicy.IsRelevant(intents, registry),
            _ => throw new ArgumentOutOfRangeException(nameof(branchType), branchType, "Unknown branch type.")
        };
    }

    private Task<BranchResult> ExecuteBranchAsync(
        BranchType branchType,
        List<IntentResult> intents,
        string message,
        AgentIntentRegistrySnapshot registry,
        AgentWorkflowSink sink,
        SessionRuntimeSnapshot? session,
        CancellationToken ct)
    {
        return branchType switch
        {
            BranchType.Knowledge => knowledgeRetrieval.ExecuteAsync(intents, registry, ct),
            BranchType.DataAnalysis => dataAnalysis.ExecuteAsync(intents, registry, sink, session, ct),
            BranchType.BusinessPolicy => businessPolicy.ExecuteAsync(intents, message, registry, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(branchType), branchType, "Unknown branch type.")
        };
    }

}

internal sealed class FinalAgentContextCompensation(
    IFinalAgentContextStore store,
    Guid sessionId,
    ILogger logger) : IAsyncDisposable
{
    private int completionState;

    public void MarkCompleted()
    {
        Interlocked.CompareExchange(ref completionState, 1, 0);
    }

    public async Task RemoveAndCompleteAsync()
    {
        if (Interlocked.CompareExchange(ref completionState, 1, 0) == 0)
        {
            await store.RemoveAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref completionState, 1, 0) == 0)
        {
            try
            {
                await store.RemoveAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Final-agent context compensation failed after the primary workflow exit. SessionId={SessionId}; CleanupErrorType={CleanupErrorType}; PrimaryOutcome=preserved",
                    sessionId,
                    exception.GetType().Name);
            }
        }
    }
}
