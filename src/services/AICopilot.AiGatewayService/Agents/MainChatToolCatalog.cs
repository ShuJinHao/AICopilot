using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.BusinessQueries;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Agents;

public sealed class MainChatToolCatalog(
    IAgentPluginCatalog pluginCatalog,
    MainChatToolGate toolGate,
    BusinessQueryExecutor businessQueryExecutor,
    IKnowledgeBaseReadService knowledgeBaseReadService,
    IKnowledgeRetrievalService knowledgeRetrievalService)
{
    public async Task<AiToolDefinition[]> BuildAsync(
        SessionRuntimeSnapshot session,
        string userMessage,
        TrustedRenderChunkBuffer renderChunkBuffer,
        CancellationToken cancellationToken)
    {
        BusinessQueryContext? confirmedQuery = null;
        if (businessQueryExecutor.TryConfirmPending(
                session.Id,
                userMessage,
                out var restoredConfirmation))
        {
            confirmedQuery = restoredConfirmation;
        }

        var pluginTools = pluginCatalog.GetAllPlugin()
            .Where(plugin => plugin.ChatExposureMode.CanExposeInChat())
            .SelectMany(plugin => pluginCatalog.GetPluginTools(plugin.Name))
            .ToArray();
        var permittedTools = (await toolGate.FilterRegisteredAsync(
                pluginTools,
                cancellationToken))
            .ToList();

        if (await toolGate.CanExposeFixedAsync(cancellationToken))
        {
            permittedTools.Add(MainChatBusinessQueryTool.CreateDefinition(
                new MainChatBusinessQueryTool(
                    businessQueryExecutor,
                    session,
                    confirmedQuery,
                    renderChunkBuffer)));
        }

        if (await toolGate.CanExposeFixedAsync(
                cancellationToken,
                "Rag.SearchKnowledgeBase"))
        {
            var authorizedKnowledgeBases = await knowledgeBaseReadService.ListAsync(
                cancellationToken);
            if (authorizedKnowledgeBases.Count > 0)
            {
                permittedTools.Add(MainChatKnowledgeQueryTool.CreateDefinition(
                    new MainChatKnowledgeQueryTool(
                        knowledgeRetrievalService,
                        authorizedKnowledgeBases),
                    authorizedKnowledgeBases));
            }
        }

        return permittedTools.ToArray();
    }
}

internal sealed class AgentSessionCheckpointSink(
    IAgentSessionStateStore store,
    Guid sessionId,
    Guid userId,
    string? tenantId,
    Guid turnId) : IAgentSessionCheckpointSink
{
    public async ValueTask PersistAsync(
        string serializedSessionState,
        CancellationToken cancellationToken = default)
    {
        _ = await store.PersistCheckpointAsync(
            sessionId,
            userId,
            tenantId,
            turnId,
            serializedSessionState,
            cancellationToken);
    }
}
