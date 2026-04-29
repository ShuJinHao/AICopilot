using System.Runtime.CompilerServices;
using System.Text;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.Workflows;

public class ChatWorkflowOrchestrator(
    IntentRoutingExecutor intentRouting,
    ToolsPackExecutor toolsPack,
    KnowledgeRetrievalExecutor knowledgeRetrieval,
    DataAnalysisExecutor dataAnalysis,
    BusinessPolicyExecutor businessPolicy,
    ContextAggregatorExecutor contextAggregator,
    FinalAgentBuildExecutor agentBuild,
    FinalAgentRunExecutor agentRun,
    IFinalAgentContextStore finalAgentContextStore,
    IFinalAgentContextSerializer finalAgentContextSerializer)
{
    public async IAsyncEnumerable<ChatChunk> RunIntentWorkflowAsync(
        ChatStreamRequest request,
        SessionRuntimeSnapshot? session,
        StringBuilder assistantText,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var routing = await intentRouting.ExecuteAsync(request, ct);
        if (!string.IsNullOrWhiteSpace(routing.ResponseText))
        {
            yield return new ChatChunk(IntentRoutingExecutor.ExecutorId, ChunkType.Intent, routing.ResponseText);
        }

        var sink = new ChatWorkflowSink();
        var branchTasks = new[]
        {
            toolsPack.ExecuteAsync(routing.Intents, ct),
            knowledgeRetrieval.ExecuteAsync(routing.Intents, ct),
            dataAnalysis.ExecuteAsync(routing.Intents, sink, session, ct),
            businessPolicy.ExecuteAsync(routing.Intents, request.Message, ct)
        };

        var allBranchesTask = Task.WhenAll(branchTasks);
        _ = allBranchesTask.ContinueWith(
            task => sink.Complete(task.Exception),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

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
}
