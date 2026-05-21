using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.PromptPolicy;
using AICopilot.Core.AiGateway.Aggregates.RoutingModel;
using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.TrialOperations;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.EntityFrameworkCore.Configuration.AiGateway;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AICopilot.EntityFrameworkCore;

public sealed class AiGatewayDbContext(DbContextOptions<AiGatewayDbContext> options) : DbContext(options)
{
    public DbSet<LanguageModel> LanguageModels => Set<LanguageModel>();

    public DbSet<ConversationTemplate> ConversationTemplates => Set<ConversationTemplate>();

    public DbSet<ApprovalPolicy> ApprovalPolicies => Set<ApprovalPolicy>();

    public DbSet<PromptPolicy> PromptPolicies => Set<PromptPolicy>();

    public DbSet<RoutingModelConfiguration> RoutingModelConfigurations => Set<RoutingModelConfiguration>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();

    public DbSet<AgentTaskRunAttempt> AgentTaskRunAttempts => Set<AgentTaskRunAttempt>();

    public DbSet<AgentTaskRunQueueItem> AgentTaskRunQueueItems => Set<AgentTaskRunQueueItem>();

    public DbSet<AgentWorkerHeartbeat> AgentWorkerHeartbeats => Set<AgentWorkerHeartbeat>();

    public DbSet<ArtifactWorkspace> ArtifactWorkspaces => Set<ArtifactWorkspace>();

    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    public DbSet<ChatRuntimeSettings> ChatRuntimeSettings => Set<ChatRuntimeSettings>();

    public DbSet<UploadRecord> UploadRecords => Set<UploadRecord>();

    public DbSet<ToolRegistration> ToolRegistrations => Set<ToolRegistration>();

    public DbSet<ToolExecutionRecord> ToolExecutionRecords => Set<ToolExecutionRecord>();

    public DbSet<TrialCampaign> TrialCampaigns => Set<TrialCampaign>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("aigateway");
        builder.ApplyConfiguration(new LanguageModelConfiguration());
        builder.ApplyConfiguration(new ConversationTemplateConfiguration());
        builder.ApplyConfiguration(new ApprovalPolicyConfiguration());
        builder.ApplyConfiguration(new PromptPolicyConfiguration());
        builder.ApplyConfiguration(new RoutingModelConfigurationConfiguration());
        builder.ApplyConfiguration(new SessionConfiguration());
        builder.ApplyConfiguration(new MessageConfiguration());
        builder.ApplyConfiguration(new AgentTaskConfiguration());
        builder.ApplyConfiguration(new AgentTaskRunAttemptConfiguration());
        builder.ApplyConfiguration(new AgentTaskRunQueueItemConfiguration());
        builder.ApplyConfiguration(new AgentWorkerHeartbeatConfiguration());
        builder.ApplyConfiguration(new AgentStepConfiguration());
        builder.ApplyConfiguration(new ArtifactWorkspaceConfiguration());
        builder.ApplyConfiguration(new ArtifactConfiguration());
        builder.ApplyConfiguration(new ApprovalRequestConfiguration());
        builder.ApplyConfiguration(new ChatRuntimeSettingsConfiguration());
        builder.ApplyConfiguration(new UploadRecordConfiguration());
        builder.ApplyConfiguration(new ToolRegistrationConfiguration());
        builder.ApplyConfiguration(new ToolExecutionRecordConfiguration());
        builder.ApplyConfiguration(new TrialCampaignConfiguration());
        builder.ApplyConfiguration(new TrialScenarioRunConfiguration());
        builder.ApplyConfiguration(new TrialRiskIssueConfiguration());
        builder.ApplyConfiguration(new OutboxMessageConfiguration());
        builder.Entity<OutboxMessage>().ToTable(
            "outbox_messages",
            "outbox",
            tableBuilder => tableBuilder.ExcludeFromMigrations());
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var domainEventEntities = ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .OfType<IHasDomainEvents>()
            .Where(entity => entity.DomainEvents.Count > 0)
            .ToArray();

        var domainEvents = domainEventEntities
            .SelectMany(entity => entity.DomainEvents)
            .ToArray();

        OutboxMessages.AddRange(domainEvents.Select(OutboxMessage.FromIntegrationEvent));

        var result = await base.SaveChangesAsync(cancellationToken);

        foreach (var entity in domainEventEntities)
        {
            entity.ClearDomainEvents();
        }

        return result;
    }
}

public sealed class AiGatewayDbContextFactory : IDesignTimeDbContextFactory<AiGatewayDbContext>
{
    public AiGatewayDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ai-copilot")
                               ?? Environment.GetEnvironmentVariable("AICOPILOT__DESIGNTIME__CONNECTION")
                               ?? throw new InvalidOperationException(
                                   "Design-time migration requires ConnectionStrings__ai-copilot or AICOPILOT__DESIGNTIME__CONNECTION.");

        var optionsBuilder = new DbContextOptionsBuilder<AiGatewayDbContext>();
        optionsBuilder.UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.AiGateway);

        return new AiGatewayDbContext(optionsBuilder.Options);
    }
}
