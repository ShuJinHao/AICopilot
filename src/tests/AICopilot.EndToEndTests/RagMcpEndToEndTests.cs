using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Plugins;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Security;
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.Infrastructure.Mcp;
using AICopilot.McpService;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;


namespace AICopilot.EndToEndTests;

[Collection(CloudSemanticSimulationBackendTestCollection.Name)]
public sealed class RagMcpEndToEndTests : EndToEndScenarioTestBase
{
    public RagMcpEndToEndTests(CloudSemanticSimulationAICopilotAppFixture fixture)
        : base(fixture)
    {
    }

    public async Task RagAndMcpSmoke_ShouldIndexDocument_SearchContent_AndLoadOnlyEnabledMcp()
    {
        await AuthenticateAsAdminAsync();

        var embeddingModelId = await CreateEmbeddingModelAsync(
            $"rag-embedding-{Guid.NewGuid():N}",
            "fake-embedding-model",
            BuildFakeAiBaseUrl(),
            "sk-rag");

        var knowledgeBaseId = await CreateKnowledgeBaseAsync(
            $"rag-kb-{Guid.NewGuid():N}",
            "RAG smoke",
            embeddingModelId);

        var searchKeyword = $"rag-mcp-keyword-{Guid.NewGuid():N}";
        var upload = await UploadDocumentAsync(
            knowledgeBaseId,
            "rag-mcp.txt",
            $"This is a RAG smoke document.\n{searchKeyword}\n");

        await EventuallyAsync(async () =>
        {
            var documents = await GetJsonAsync<List<KnowledgeDocumentDto>>($"/api/rag/document/list?knowledgeBaseId={knowledgeBaseId}");
            return documents.Single(document => document.Id == upload.Id);
        }, document => document.Status == DocumentStatus.Indexed);

        var searchResults = await PostJsonAsync<List<SearchKnowledgeBaseResult>>("/api/rag/search", new
        {
            knowledgeBaseId,
            queryText = searchKeyword,
            topK = 3,
            minScore = 0.0
        });

        searchResults.Should().Contain(result => result.Text.Contains(searchKeyword));

        var enabledServerName = $"mcp-enabled-{Guid.NewGuid():N}";
        var disabledServerName = $"mcp-disabled-{Guid.NewGuid():N}";
        var mcpServerPath = typeof(TestingMcpServerMarker).Assembly.Location;

        var mcpServerCommand = GetTestingMcpExecutablePath();

        await CreateMcpServerAsync(
            enabledServerName,
            true,
            mcpServerCommand,
            string.Empty,
            ChatExposureMode.Advisory,
            ["Echo"]);
        await CreateMcpServerAsync(
            disabledServerName,
            false,
            mcpServerCommand,
            string.Empty,
            ChatExposureMode.Advisory,
            ["Echo"]);

        var connectionString = await _fixture.GetConnectionStringAsync();
        using var verificationHost = BuildMcpVerificationHost(connectionString);
        await using var scope = verificationHost.Services.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<IMcpServerBootstrap>();
        var pluginLoader = scope.ServiceProvider.GetRequiredService<AgentPluginLoader>();

        var clients = new List<IAsyncDisposable>();
        await foreach (var client in bootstrap.StartAsync(CancellationToken.None))
        {
            clients.Add(client);
        }

        try
        {
            clients.Should().HaveCount(1);
            pluginLoader.GetPlugin(enabledServerName).Should().NotBeNull();
            pluginLoader.GetPlugin(disabledServerName).Should().BeNull();
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }
}
