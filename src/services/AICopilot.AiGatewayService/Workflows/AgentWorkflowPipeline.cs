using System.Runtime.CompilerServices;
using System.Text;
using AICopilot.AiGatewayService.Agents;
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
    ChatExecutionMetadataSnapshot ExecutionMetadata);

public class AgentWorkflowPipeline(
    IntentRoutingExecutor intentRouting,
    ToolsPackExecutor toolsPack,
    KnowledgeRetrievalExecutor knowledgeRetrieval,
    DataAnalysisExecutor dataAnalysis,
    BusinessPolicyExecutor businessPolicy,
    ContextAggregatorExecutor contextAggregator,
    FinalAgentBuildExecutor agentBuild,
    FinalAgentRunExecutor agentRun,
    IFinalAgentContextStore finalAgentContextStore,
    IFinalAgentContextSerializer finalAgentContextSerializer,
    ILogger<AgentWorkflowPipeline> logger)
{
    public async Task<AgentPlanDraftWorkflowResult> RunPlanDraftWorkflowAsync(
        ChatStreamRequest request,
        CancellationToken ct = default)
    {
        var routing = await intentRouting.ExecuteAsync(request, ct);
        var tools = await DiscoverSafeToolsAsync(routing.Intents, ct);
        return new AgentPlanDraftWorkflowResult(
            routing.Scene.ToString(),
            routing.Intents,
            tools.Tools ?? [],
            routing.ExecutionMetadata);
    }

    public async IAsyncEnumerable<ChatChunk> RunIntentWorkflowAsync(
        ChatStreamRequest request,
        SessionRuntimeSnapshot? session,
        StringBuilder assistantText,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var routing = await intentRouting.ExecuteAsync(request, ct);
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

        var sink = new AgentWorkflowSink();
        using var branchCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var branchToken = branchCancellation.Token;
        var branchTasks = AgentWorkflowTopology.ParallelBranches
            .OrderBy(branch => branch.Order)
            .Select(branch =>
            {
                var isRequired = IsBranchRequired(branch.BranchType, routing.Intents);
                return RunBranchSafelyAsync(
                    branch.BranchType,
                    isRequired,
                    () => ExecuteBranchAsync(branch.BranchType, routing.Intents, request.Message, sink, session, branchToken),
                    logger,
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

            var generationContext = contextAggregator.Execute(request, routing.Scene, branchResults);

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

    protected virtual bool IsBranchRequired(BranchType branchType, IReadOnlyCollection<IntentResult> intents)
    {
        return branchType switch
        {
            BranchType.Tools => ToolsPackExecutor.IsRelevant(intents),
            BranchType.Knowledge => KnowledgeRetrievalExecutor.IsRelevant(intents),
            BranchType.DataAnalysis => DataAnalysisExecutor.IsRelevant(intents),
            BranchType.BusinessPolicy => businessPolicy.IsRelevant(intents),
            _ => throw new ArgumentOutOfRangeException(nameof(branchType), branchType, "Unknown branch type.")
        };
    }

    protected virtual Task<BranchResult> ExecuteBranchAsync(
        BranchType branchType,
        List<IntentResult> intents,
        string message,
        AgentWorkflowSink sink,
        SessionRuntimeSnapshot? session,
        CancellationToken ct)
    {
        return branchType switch
        {
            BranchType.Tools => DiscoverSafeToolsAsync(intents, ct),
            BranchType.Knowledge => knowledgeRetrieval.ExecuteAsync(intents, ct),
            BranchType.DataAnalysis => dataAnalysis.ExecuteAsync(intents, sink, session, ct),
            BranchType.BusinessPolicy => businessPolicy.ExecuteAsync(intents, message, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(branchType), branchType, "Unknown branch type.")
        };
    }

    private async Task<BranchResult> DiscoverSafeToolsAsync(
        List<IntentResult> intents,
        CancellationToken ct)
    {
        var discovered = await toolsPack.DiscoverAsync(intents, ct);
        if (discovered.Tools is null)
        {
            return discovered;
        }

        var safeTools = discovered.Tools.Where(tool =>
        {
            var decision = tool.Kind == AiToolCallKind.Mcp || tool.TargetType == AiToolTargetType.McpServer
                ? AiToolSafetyPolicy.EvaluateConfiguredMcp(tool)
                : AiToolSafetyPolicy.EvaluateConfigured(
                tool.ReadOnlyDeclared,
                tool.McpReadOnlyHint,
                tool.McpDestructiveHint,
                tool.McpIdempotentHint,
                tool.CapabilityKind,
                tool.ExternalSystemType,
                tool.RiskLevel,
                tool.ToolName ?? tool.Name,
                tool.Description,
                tool.JsonSchema,
                tool.ReturnJsonSchema);
            if (!decision.IsAllowed)
            {
                logger.LogWarning(
                    "Agent workflow excluded unsafe tool {ToolName}. Reasons={Reasons}",
                    tool.Name,
                    string.Join("; ", decision.BlockReasons));
            }

            return decision.IsAllowed;
        }).ToArray();

        return BranchResult.FromTools(safeTools);
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
