using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.RoutingModel;
using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.EntityFrameworkCore.Configuration.AiGateway;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AICopilot.EntityFrameworkCore;

public sealed class AiGatewayDbContext(DbContextOptions<AiGatewayDbContext> options) : DbContext(options)
{
    public DbSet<LanguageModel> LanguageModels => Set<LanguageModel>();

    public DbSet<ConversationTemplate> ConversationTemplates => Set<ConversationTemplate>();

    public DbSet<ApprovalPolicy> ApprovalPolicies => Set<ApprovalPolicy>();

    public DbSet<RoutingModelConfiguration> RoutingModelConfigurations => Set<RoutingModelConfiguration>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<MessageEvent> MessageEvents => Set<MessageEvent>();

    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();

    public DbSet<AgentTaskRunAttempt> AgentTaskRunAttempts => Set<AgentTaskRunAttempt>();

    public DbSet<AgentTaskRunQueueItem> AgentTaskRunQueueItems => Set<AgentTaskRunQueueItem>();

    public DbSet<AgentNodeRun> AgentNodeRuns => Set<AgentNodeRun>();

    public DbSet<AgentEvidenceRecord> AgentEvidenceRecords => Set<AgentEvidenceRecord>();

    public DbSet<AgentRunUsageLedgerEntry> AgentRunUsageLedgerEntries => Set<AgentRunUsageLedgerEntry>();

    public DbSet<AgentNodeReconciliationDecision> AgentNodeReconciliationDecisions => Set<AgentNodeReconciliationDecision>();

    public DbSet<ModelQuotaReservation> ModelQuotaReservations => Set<ModelQuotaReservation>();

    public DbSet<ArtifactFileSetOperation> ArtifactFileSetOperations => Set<ArtifactFileSetOperation>();

    public DbSet<AgentWorkerHeartbeat> AgentWorkerHeartbeats => Set<AgentWorkerHeartbeat>();

    public DbSet<ArtifactWorkspace> ArtifactWorkspaces => Set<ArtifactWorkspace>();

    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    public DbSet<ChatRuntimeSettings> ChatRuntimeSettings => Set<ChatRuntimeSettings>();

    public DbSet<UploadRecord> UploadRecords => Set<UploadRecord>();

    public DbSet<ToolRegistration> ToolRegistrations => Set<ToolRegistration>();

    public DbSet<ToolExecutionRecord> ToolExecutionRecords => Set<ToolExecutionRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("aigateway");
        builder.HasSequence<long>("model_quota_fencing_seq", "aigateway");
        builder.ApplyConfiguration(new LanguageModelConfiguration());
        builder.ApplyConfiguration(new ConversationTemplateConfiguration());
        builder.ApplyConfiguration(new ApprovalPolicyConfiguration());
        builder.ApplyConfiguration(new RoutingModelConfigurationConfiguration());
        builder.ApplyConfiguration(new SessionConfiguration());
        builder.ApplyConfiguration(new MessageConfiguration());
        builder.ApplyConfiguration(new MessageEventConfiguration());
        builder.ApplyConfiguration(new AgentTaskConfiguration());
        builder.ApplyConfiguration(new AgentTaskRunAttemptConfiguration());
        builder.ApplyConfiguration(new AgentTaskRunQueueItemConfiguration());
        builder.ApplyConfiguration(new AgentNodeRunConfiguration());
        builder.ApplyConfiguration(new AgentEvidenceRecordConfiguration());
        builder.ApplyConfiguration(new AgentRunUsageLedgerEntryConfiguration());
        builder.ApplyConfiguration(new AgentNodeReconciliationDecisionConfiguration());
        builder.ApplyConfiguration(new ModelQuotaReservationConfiguration());
        builder.ApplyConfiguration(new ArtifactFileSetOperationConfiguration());
        builder.ApplyConfiguration(new AgentWorkerHeartbeatConfiguration());
        builder.ApplyConfiguration(new AgentStepConfiguration());
        builder.ApplyConfiguration(new ArtifactWorkspaceConfiguration());
        builder.ApplyConfiguration(new ArtifactConfiguration());
        builder.ApplyConfiguration(new ApprovalRequestConfiguration());
        builder.ApplyConfiguration(new ChatRuntimeSettingsConfiguration());
        builder.ApplyConfiguration(new UploadRecordConfiguration());
        builder.ApplyConfiguration(new ToolRegistrationConfiguration());
        builder.ApplyConfiguration(new ToolExecutionRecordConfiguration());
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
