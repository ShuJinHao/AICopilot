using AICopilot.Dapper;
using AICopilot.AiRuntime;
using AICopilot.Embedding;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EventBus;
using AICopilot.Infrastructure.AiGateway;
using AICopilot.Infrastructure.Authentication;
using AICopilot.Infrastructure.CloudIdentity;
using AICopilot.Infrastructure.CloudRead;
using AICopilot.Infrastructure.Mcp;
using AICopilot.Infrastructure.Rag;
using AICopilot.Infrastructure.Rag.Parsers;
using AICopilot.Infrastructure.Rag.TokenCounter;
using AICopilot.Infrastructure.Security;
using AICopilot.Infrastructure.Storage;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace AICopilot.Infrastructure;

public static class DependencyInjection
{
    public static void AddInfrastructures(this IHostApplicationBuilder builder)
    {
        builder.AddSecretProtection();
        builder.AddEfCore();
        builder.AddDapper();
        builder.AddEmbedding();
        builder.AddEventBus();
        builder.AddAiRuntime();

        AddLocalFileStorage(builder.Services);
        builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        builder.Services.AddHttpClient<ICloudIdentityStatusClient, CloudIdentityStatusClient>();
        builder.Services.AddHttpClient<ICloudAiReadClient, CloudAiReadClient>();
        builder.Services.AddScoped<IChatClientProvider, OpenAiChatClientProvider>();
        builder.Services.AddScoped<IChatClientProvider, AnthropicChatClientProvider>();
        builder.Services.AddTransient<AiProviderRetryHandler>();
        builder.Services.AddScoped<ILanguageModelConnectivityTester, LanguageModelConnectivityTester>();
        builder.AddDocumentParsers();
        builder.Services.AddSingleton<ISessionExecutionLock>(serviceProvider =>
        {
            var connectionString = builder.Configuration.GetConnectionString("ai-copilot");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("PostgreSQL session execution lock requires ConnectionStrings:ai-copilot.");
            }

            serviceProvider.GetRequiredService<ILogger<PostgreSqlSessionExecutionLock>>()
                .LogInformation("Using PostgreSQL advisory session execution lock.");

            return new PostgreSqlSessionExecutionLock(
                connectionString,
                serviceProvider.GetRequiredService<ILogger<PostgreSqlSessionExecutionLock>>());
        });
        builder.AddMcpRuntime();
        builder.Services.AddHttpClient("OpenAI", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddHttpMessageHandler<AiProviderRetryHandler>();
        builder.Services.AddHttpClient("Anthropic", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddHttpMessageHandler<AiProviderRetryHandler>();
    }

    private static void AddMcpRuntime(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<McpRuntimeOptions>(builder.Configuration.GetSection("Mcp:Runtime"));

        var enabled = builder.Configuration.GetValue("Mcp:Runtime:Enabled", false);
        if (!enabled)
        {
            return;
        }

        builder.Services.AddScoped<McpServerBootstrap>();
        builder.Services.AddScoped<IMcpServerBootstrap>(sp => sp.GetRequiredService<McpServerBootstrap>());
        builder.Services.AddScoped<IMcpRuntimeRegistrationProvider>(sp => sp.GetRequiredService<McpServerBootstrap>());
        builder.Services.AddScoped<McpToolRegistrySynchronizer>();
        builder.Services.AddSingleton<McpRuntimeRegistrySynchronizer>();
        builder.Services.AddHostedService<McpServerManager>();
    }

    public static void AddRagWorkerInfrastructure(
        this IHostApplicationBuilder builder,
        Assembly consumerAssembly)
    {
        builder.AddSecretProtection();
        builder.AddEfCore();
        builder.AddEventBus(consumerAssembly);
        builder.AddEmbedding();

        AddLocalFileStorage(builder.Services);
        builder.AddDocumentParsers();
        builder.Services.AddSingleton<ITokenCounter, SharpTokenCounter>();
        builder.Services.AddSingleton<IDocumentTextSplitter, TextSplitterService>();
        builder.Services.AddScoped<IDocumentContentExtractor, DocumentContentExtractor>();
        builder.Services.AddScoped<IKnowledgeVectorIndexWriter, KnowledgeVectorIndexWriter>();
    }

    private static void AddSecretProtection(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ISecretProtector, SecretProtector>();
    }

    private static void AddLocalFileStorage(IServiceCollection services)
    {
        services.AddSingleton<LocalFileStorageService>();
        services.AddSingleton<IFileStorageService>(provider =>
            provider.GetRequiredService<LocalFileStorageService>());
        services.AddSingleton<IPersistenceFileReconciliationJournal>(provider =>
            provider.GetRequiredService<LocalFileStorageService>());
        services.AddScoped<IPersistenceFileReconciliationLeaseManager,
            PostgresPersistenceFileReconciliationLeaseManager>();
        services.AddScoped<IPersistenceFileStorageService, LocalPersistenceFileStorageService>();
    }

    private static void AddDocumentParsers(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IDocumentParser, PdfDocumentParser>();
        builder.Services.AddSingleton<IDocumentParser, TextDocumentParser>();
        builder.Services.AddSingleton<DocumentParserFactory>();
        builder.Services.AddSingleton<IDocumentFormatPolicy>(sp =>
            sp.GetRequiredService<DocumentParserFactory>());
    }

}
