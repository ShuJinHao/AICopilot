using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AICopilot.RagService.Documents;
using AICopilot.RagService.KnowledgeBases;
using AICopilot.Services.Contracts;
using System.Reflection;

namespace AICopilot.RagService;

public static class DependencyInjection
{
    public static void AddRagService(this IHostApplicationBuilder builder)
    {
        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
        });

        builder.Services.AddScoped<IKnowledgeRetrievalService, KnowledgeRetrievalService>();
        builder.Services.AddScoped<IKnowledgeBaseReadService, KnowledgeBaseReadService>();
    }

    public static void AddRagIndexingService(this IHostApplicationBuilder builder)
    {
        builder.Services.AddScoped<IDocumentIndexingService, DocumentIndexingService>();
    }
}
