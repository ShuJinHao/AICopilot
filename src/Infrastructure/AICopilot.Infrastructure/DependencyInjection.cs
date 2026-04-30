using AICopilot.Dapper;
using AICopilot.AiRuntime;
using AICopilot.Embedding;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.EventBus;
using AICopilot.Infrastructure.AiGateway;
using AICopilot.Infrastructure.Authentication;
using AICopilot.Infrastructure.Mcp;
using AICopilot.Infrastructure.Rag;
using AICopilot.Infrastructure.Rag.Parsers;
using AICopilot.Infrastructure.Rag.TokenCounter;
using AICopilot.Infrastructure.Storage;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection;

namespace AICopilot.Infrastructure;

public static class DependencyInjection
{
    public static void AddInfrastructures(this IHostApplicationBuilder builder)
    {
        builder.AddEfCore();
        builder.AddDapper();
        builder.AddEmbedding();
        builder.AddEventBus();
        builder.Services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();
        builder.AddAiRuntime();

        builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        builder.Services.AddScoped<IChatClientProvider, OpenAiChatClientProvider>();
        builder.AddMcpRuntime();
        builder.Services.AddHttpClient("OpenAI", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        builder.AddFinalAgentContextStore();
    }

    private static void AddMcpRuntime(this IHostApplicationBuilder builder)
    {
        var enabled = builder.Configuration.GetValue("Mcp:Runtime:Enabled", true);
        if (!enabled)
        {
            return;
        }

        builder.Services.AddScoped<IMcpServerBootstrap, McpServerBootstrap>();
        builder.Services.AddHostedService<McpServerManager>();
    }

    public static void AddRagWorkerInfrastructure(
        this IHostApplicationBuilder builder,
        Assembly consumerAssembly)
    {
        builder.AddEfCore();
        builder.AddEventBus(consumerAssembly);
        builder.AddEmbedding();
        builder.Services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();

        builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        builder.Services.AddSingleton<IDocumentParser, PdfDocumentParser>();
        builder.Services.AddSingleton<IDocumentParser, TextDocumentParser>();
        builder.Services.AddSingleton<DocumentParserFactory>();
        builder.Services.AddSingleton<ITokenCounter, SharpTokenCounter>();
        builder.Services.AddSingleton<IDocumentTextSplitter, TextSplitterService>();
        builder.Services.AddScoped<IDocumentContentExtractor, DocumentContentExtractor>();
        builder.Services.AddScoped<IKnowledgeVectorIndexWriter, KnowledgeVectorIndexWriter>();
    }

    private static void AddFinalAgentContextStore(this IHostApplicationBuilder builder)
    {
        var provider = builder.Configuration["AiGateway:FinalAgentContextStore:Provider"];
        if (string.Equals(provider, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            var redisConnectionString = builder.Configuration.GetConnectionString("final-agent-context-redis");
            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                throw new InvalidOperationException("AiGateway:FinalAgentContextStore:Provider 配置为 Redis 时，必须提供 ConnectionStrings:final-agent-context-redis。");
            }

            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "aicopilot:";
            });
            builder.Services.AddSingleton<IFinalAgentContextStore, RedisFinalAgentContextStore>();
        }
        else
        {
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<IFinalAgentContextStore, MemoryCacheFinalAgentContextStore>();
        }
    }
}
