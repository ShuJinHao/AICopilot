using System.Runtime.CompilerServices;
using System.Text;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
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
        var tools = await toolsPack.DiscoverAsync(routing.Intents, ct);
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
        var branchTasks = AgentWorkflowTopology.ParallelBranches
            .OrderBy(branch => branch.Order)
            .Select(branch => RunBranchSafelyAsync(
                branch.BranchType,
                () => ExecuteBranchAsync(branch.BranchType, routing.Intents, request.Message, sink, session, ct),
                ct))
            .ToArray();

        var allBranchesTask = Task.WhenAll(branchTasks);
        _ = CompleteSinkWhenBranchesFinishAsync(allBranchesTask, sink);

        await foreach (var chunk in sink.ReadAllAsync(ct))
        {
            yield return chunk;
        }

        var branchResults = await allBranchesTask;
        var generationContext = contextAggregator.Execute(request, routing.Scene, branchResults);

        await using var finalAgentContext = await agentBuild.ExecuteAsync(generationContext, ct);
        await foreach (var chunk in RunFinalAgentAsync(finalAgentContext, session, assistantText, ct))
        {
            yield return chunk;
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
        var completed = false;
        try
        {
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
            }
            else
            {
                await finalAgentContextStore.RemoveAsync(agentContext.SessionId, ct);
            }

            completed = true;
        }
        finally
        {
            if (!completed)
            {
                await finalAgentContextStore.RemoveAsync(agentContext.SessionId, CancellationToken.None);
            }
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

    private async Task<BranchResult> RunBranchSafelyAsync(
        BranchType branchType,
        Func<Task<BranchResult>> execute,
        CancellationToken ct)
    {
        try
        {
            return await execute().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Agent workflow branch {BranchType} failed; continuing with an empty branch result. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                branchType,
                ex.GetType().Name);
            return CreateEmptyBranchResult(branchType);
        }
    }

    private static BranchResult CreateEmptyBranchResult(BranchType branchType)
    {
        return branchType switch
        {
            BranchType.Tools => BranchResult.FromTools([]),
            BranchType.Knowledge => BranchResult.FromKnowledge(string.Empty),
            BranchType.DataAnalysis => BranchResult.FromDataAnalysis(string.Empty),
            BranchType.BusinessPolicy => BranchResult.FromBusinessPolicy(string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(branchType), branchType, "Unknown branch type.")
        };
    }

    private Task<BranchResult> ExecuteBranchAsync(
        BranchType branchType,
        List<IntentResult> intents,
        string message,
        AgentWorkflowSink sink,
        SessionRuntimeSnapshot? session,
        CancellationToken ct)
    {
        return branchType switch
        {
            BranchType.Tools => toolsPack.ExecuteAsync(intents, ct),
            BranchType.Knowledge => knowledgeRetrieval.ExecuteAsync(intents, ct),
            BranchType.DataAnalysis => dataAnalysis.ExecuteAsync(intents, sink, session, ct),
            BranchType.BusinessPolicy => businessPolicy.ExecuteAsync(intents, message, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(branchType), branchType, "Unknown branch type.")
        };
    }
}
