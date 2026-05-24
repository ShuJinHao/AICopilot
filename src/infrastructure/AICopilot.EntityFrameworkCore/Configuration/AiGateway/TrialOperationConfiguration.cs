using AICopilot.Core.AiGateway.Aggregates.TrialOperations;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class TrialCampaignConfiguration : IEntityTypeConfiguration<TrialCampaign>
{
    public void Configure(EntityTypeBuilder<TrialCampaign> builder)
    {
        builder.ToTable("trial_campaigns");

        builder.HasKey(campaign => campaign.Id);
        builder.Property(campaign => campaign.Id)
            .HasConversion(id => id.Value, value => new TrialCampaignId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(campaign => campaign.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("name");

        builder.Property(campaign => campaign.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("status");

        builder.Property(campaign => campaign.AllowedSourceModes)
            .IsRequired()
            .HasColumnType("text[]")
            .HasColumnName("allowed_source_modes");

        builder.Property(campaign => campaign.OwnerDepartment)
            .HasMaxLength(160)
            .HasColumnName("owner_department");

        builder.Property(campaign => campaign.StartAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("start_at");

        builder.Property(campaign => campaign.EndAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("end_at");

        builder.Property(campaign => campaign.Summary)
            .HasMaxLength(2000)
            .HasColumnName("summary");

        builder.Property(campaign => campaign.PilotReadinessStatus)
            .HasConversion<string>()
            .HasMaxLength(60)
            .IsRequired()
            .HasColumnName("pilot_readiness_status");

        builder.Property(campaign => campaign.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(campaign => campaign.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");

        builder.HasIndex(campaign => campaign.Status);
        builder.HasIndex(campaign => campaign.CreatedAt);
        builder.HasIndex(campaign => campaign.PilotReadinessStatus);

        builder.HasMany(campaign => campaign.ScenarioRuns)
            .WithOne()
            .HasForeignKey(run => run.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(campaign => campaign.RiskIssues)
            .WithOne()
            .HasForeignKey(issue => issue.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(campaign => campaign.ScenarioRuns)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(campaign => campaign.RiskIssues)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class TrialScenarioRunConfiguration : IEntityTypeConfiguration<TrialScenarioRun>
{
    public void Configure(EntityTypeBuilder<TrialScenarioRun> builder)
    {
        builder.ToTable("trial_scenario_runs");

        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id)
            .HasConversion(id => id.Value, value => new TrialScenarioRunId(value))
            .HasColumnName("id");

        builder.Property(run => run.CampaignId)
            .HasConversion(id => id.Value, value => new TrialCampaignId(value))
            .IsRequired()
            .HasColumnName("campaign_id");

        builder.Property(run => run.ScenarioId)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("scenario_id");

        builder.Property(run => run.TrialMode)
            .IsRequired()
            .HasMaxLength(80)
            .HasColumnName("trial_mode");

        builder.Property(run => run.SourceMode)
            .IsRequired()
            .HasMaxLength(80)
            .HasColumnName("source_mode");

        builder.Property(run => run.Boundary)
            .HasMaxLength(120)
            .HasColumnName("boundary");

        builder.Property(run => run.TaskId)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .IsRequired()
            .HasColumnName("task_id");

        builder.Property(run => run.ArtifactIds)
            .IsRequired()
            .HasColumnType("uuid[]")
            .HasColumnName("artifact_ids");

        builder.Property(run => run.QueryHashes)
            .IsRequired()
            .HasColumnType("text[]")
            .HasColumnName("query_hashes");

        builder.Property(run => run.ResultHashes)
            .IsRequired()
            .HasColumnType("text[]")
            .HasColumnName("result_hashes");

        builder.Property(run => run.ApprovalStatus)
            .IsRequired()
            .HasMaxLength(80)
            .HasColumnName("approval_status");

        builder.Property(run => run.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("status");

        builder.Property(run => run.StartedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("started_at");

        builder.Property(run => run.CompletedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("completed_at");

        builder.Property(run => run.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(run => run.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");

        builder.HasIndex(run => run.CampaignId);
        builder.HasIndex(run => run.TaskId);
        builder.HasIndex(run => run.SourceMode);
        builder.HasIndex(run => run.Status);
    }
}

public sealed class TrialRiskIssueConfiguration : IEntityTypeConfiguration<TrialRiskIssue>
{
    public void Configure(EntityTypeBuilder<TrialRiskIssue> builder)
    {
        builder.ToTable("trial_risk_issues");

        builder.HasKey(issue => issue.Id);
        builder.Property(issue => issue.Id)
            .HasConversion(id => id.Value, value => new TrialRiskIssueId(value))
            .HasColumnName("id");

        builder.Property(issue => issue.CampaignId)
            .HasConversion(id => id.Value, value => new TrialCampaignId(value))
            .IsRequired()
            .HasColumnName("campaign_id");

        builder.Property(issue => issue.Severity)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("severity");

        builder.Property(issue => issue.Category)
            .IsRequired()
            .HasMaxLength(120)
            .HasColumnName("category");

        builder.Property(issue => issue.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("status");

        builder.Property(issue => issue.Owner)
            .HasMaxLength(120)
            .HasColumnName("owner");

        builder.Property(issue => issue.SourceRef)
            .HasMaxLength(240)
            .HasColumnName("source_ref");

        builder.Property(issue => issue.ResolutionHash)
            .HasMaxLength(128)
            .HasColumnName("resolution_hash");

        builder.Property(issue => issue.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(issue => issue.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");

        builder.HasIndex(issue => issue.CampaignId);
        builder.HasIndex(issue => issue.Status);
        builder.HasIndex(issue => issue.Severity);
    }
}
