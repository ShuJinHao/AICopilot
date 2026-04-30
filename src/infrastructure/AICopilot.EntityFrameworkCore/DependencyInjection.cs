using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.EntityFrameworkCore.Security;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AICopilot.EntityFrameworkCore;

public static class DependencyInjection
{
    public static void AddEfCore(this IHostApplicationBuilder builder)
    {
        SecretStringEncryptor.EnsureConfigured();
        builder.AddNpgsqlDbContext<AiCopilotDbContext>(
            "ai-copilot",
            configureDbContextOptions: AICopilotNpgsqlOptions.ConfigureMigrationHistory(MigrationHistoryTables.AiCopilot));
        builder.AddNpgsqlDbContext<AiGatewayDbContext>(
            "ai-copilot",
            configureDbContextOptions: AICopilotNpgsqlOptions.ConfigureMigrationHistory(MigrationHistoryTables.AiGateway));
        builder.AddNpgsqlDbContext<AuditDbContext>("ai-copilot");
        builder.AddNpgsqlDbContext<DataAnalysisDbContext>(
            "ai-copilot",
            configureDbContextOptions: AICopilotNpgsqlOptions.ConfigureMigrationHistory(MigrationHistoryTables.DataAnalysis));
        builder.AddNpgsqlDbContext<IdentityStoreDbContext>(
            "ai-copilot",
            configureDbContextOptions: AICopilotNpgsqlOptions.ConfigureMigrationHistory(MigrationHistoryTables.IdentityStore));
        builder.AddNpgsqlDbContext<McpServerDbContext>(
            "ai-copilot",
            configureDbContextOptions: AICopilotNpgsqlOptions.ConfigureMigrationHistory(MigrationHistoryTables.McpServer));
        builder.AddNpgsqlDbContext<OutboxDbContext>("ai-copilot");
        builder.AddNpgsqlDbContext<RagDbContext>(
            "ai-copilot",
            configureDbContextOptions: AICopilotNpgsqlOptions.ConfigureMigrationHistory(MigrationHistoryTables.Rag));

        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped(typeof(AiGatewayRepository<>));
        builder.Services.AddScoped<IReadRepository<LanguageModel>>(provider => provider.GetRequiredService<AiGatewayRepository<LanguageModel>>());
        builder.Services.AddScoped<IRepository<LanguageModel>>(provider => provider.GetRequiredService<AiGatewayRepository<LanguageModel>>());
        builder.Services.AddScoped<IReadRepository<ConversationTemplate>>(provider => provider.GetRequiredService<AiGatewayRepository<ConversationTemplate>>());
        builder.Services.AddScoped<IRepository<ConversationTemplate>>(provider => provider.GetRequiredService<AiGatewayRepository<ConversationTemplate>>());
        builder.Services.AddScoped<IReadRepository<ApprovalPolicy>>(provider => provider.GetRequiredService<AiGatewayRepository<ApprovalPolicy>>());
        builder.Services.AddScoped<IRepository<ApprovalPolicy>>(provider => provider.GetRequiredService<AiGatewayRepository<ApprovalPolicy>>());
        builder.Services.AddScoped<IReadRepository<Session>>(provider => provider.GetRequiredService<AiGatewayRepository<Session>>());
        builder.Services.AddScoped<IRepository<Session>>(provider => provider.GetRequiredService<AiGatewayRepository<Session>>());
        builder.Services.AddScoped(typeof(RagRepository<>));
        builder.Services.AddScoped<IReadRepository<EmbeddingModel>>(provider => provider.GetRequiredService<RagRepository<EmbeddingModel>>());
        builder.Services.AddScoped<IRepository<EmbeddingModel>>(provider => provider.GetRequiredService<RagRepository<EmbeddingModel>>());
        builder.Services.AddScoped<IReadRepository<KnowledgeBase>>(provider => provider.GetRequiredService<RagRepository<KnowledgeBase>>());
        builder.Services.AddScoped<IRepository<KnowledgeBase>>(provider => provider.GetRequiredService<RagRepository<KnowledgeBase>>());
        builder.Services.AddScoped<BusinessDatabaseRepository>();
        builder.Services.AddScoped<IReadRepository<BusinessDatabase>>(provider => provider.GetRequiredService<BusinessDatabaseRepository>());
        builder.Services.AddScoped<IRepository<BusinessDatabase>>(provider => provider.GetRequiredService<BusinessDatabaseRepository>());
        builder.Services.AddScoped<McpServerRepository>();
        builder.Services.AddScoped<IReadRepository<McpServerInfo>>(provider => provider.GetRequiredService<McpServerRepository>());
        builder.Services.AddScoped<IRepository<McpServerInfo>>(provider => provider.GetRequiredService<McpServerRepository>());
        builder.Services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        builder.Services.AddScoped<IIdentityAuditLogWriter, IdentityAuditLogWriter>();
        builder.Services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();
        builder.Services.AddScoped<ITransactionalExecutionService, Transactions.EfTransactionalExecutionService>();

        builder.Services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 8;
            options.Lockout.AllowedForNewUsers = false;
        })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentityStoreDbContext>();
    }
}
