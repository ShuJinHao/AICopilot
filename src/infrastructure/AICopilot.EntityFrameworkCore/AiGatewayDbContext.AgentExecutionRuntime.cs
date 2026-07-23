using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.EntityFrameworkCore.Configuration.AiGateway;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore;

public sealed partial class AiGatewayDbContext
{
    public DbSet<AgentNodeRun> AgentNodeRuns => Set<AgentNodeRun>();
    public DbSet<AgentEvidenceRecord> AgentEvidenceRecords => Set<AgentEvidenceRecord>();
    public DbSet<AgentRunUsageLedgerEntry> AgentRunUsageLedgerEntries => Set<AgentRunUsageLedgerEntry>();
    public DbSet<AgentNodeReconciliationDecision> AgentNodeReconciliationDecisions => Set<AgentNodeReconciliationDecision>();
    public DbSet<ModelQuotaReservation> ModelQuotaReservations => Set<ModelQuotaReservation>();
    public DbSet<ArtifactFileSetOperation> ArtifactFileSetOperations => Set<ArtifactFileSetOperation>();

    private static void ApplyAgentExecutionRuntimeConfigurations(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new AgentNodeRunConfiguration());
        builder.ApplyConfiguration(new AgentEvidenceRecordConfiguration());
        builder.ApplyConfiguration(new AgentRunUsageLedgerEntryConfiguration());
        builder.ApplyConfiguration(new AgentNodeReconciliationDecisionConfiguration());
        builder.ApplyConfiguration(new ModelQuotaReservationConfiguration());
        builder.ApplyConfiguration(new ArtifactFileSetOperationConfiguration());
    }
}
