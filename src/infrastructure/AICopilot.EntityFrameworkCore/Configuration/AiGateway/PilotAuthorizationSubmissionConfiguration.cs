using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class PilotAuthorizationSubmissionConfiguration : IEntityTypeConfiguration<PilotAuthorizationSubmission>
{
    public void Configure(EntityTypeBuilder<PilotAuthorizationSubmission> builder)
    {
        builder.ToTable("pilot_authorization_submissions");

        builder.HasKey(submission => submission.Id);
        builder.Property(submission => submission.Id)
            .HasConversion(id => id.Value, value => new PilotAuthorizationSubmissionId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(submission => submission.RequestedByUserId)
            .IsRequired()
            .HasColumnName("requested_by_user_id");

        builder.Property(submission => submission.RequestedByUserName)
            .HasMaxLength(160)
            .HasColumnName("requested_by_user_name");

        builder.Property(submission => submission.Title)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("title");

        builder.Property(submission => submission.BusinessPurpose)
            .IsRequired()
            .HasMaxLength(1000)
            .HasColumnName("business_purpose");

        builder.Property(submission => submission.EndpointCodes)
            .IsRequired()
            .HasColumnName("endpoint_codes");

        builder.Property(submission => submission.MaxRows)
            .HasColumnName("max_rows");

        builder.Property(submission => submission.TimeRangeDays)
            .HasColumnName("time_range_days");

        builder.Property(submission => submission.DataOwner)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("data_owner");

        builder.Property(submission => submission.ToolOwner)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("tool_owner");

        builder.Property(submission => submission.FinalOwner)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("final_owner");

        builder.Property(submission => submission.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(80)
            .HasColumnName("status");

        builder.Property(submission => submission.MachineValidationStatus)
            .IsRequired()
            .HasMaxLength(80)
            .HasColumnName("machine_validation_status");

        builder.Property(submission => submission.MachineRejectedReasons)
            .IsRequired()
            .HasColumnName("machine_rejected_reasons");

        builder.OwnsOne(submission => submission.Review, owned =>
        {
            owned.Property(review => review.SubmittedAt)
                .HasColumnType("timestamp with time zone")
                .HasColumnName("submitted_at");
            owned.Property(review => review.ReviewStartedAt)
                .HasColumnType("timestamp with time zone")
                .HasColumnName("review_started_at");
            owned.Property(review => review.LastReviewerUserId)
                .HasColumnName("last_reviewer_user_id");
            owned.Property(review => review.LastReviewerUserName)
                .HasMaxLength(160)
                .HasColumnName("last_reviewer_user_name");
            owned.Property(review => review.LastDecisionReason)
                .HasMaxLength(1000)
                .HasColumnName("last_decision_reason");
            owned.Property(review => review.LastDecisionStatus)
                .HasMaxLength(80)
                .HasColumnName("last_decision_status");
            owned.Property(review => review.LastDecisionAt)
                .HasColumnType("timestamp with time zone")
                .HasColumnName("last_decision_at");
            owned.Property(review => review.ExpiredAt)
                .HasColumnType("timestamp with time zone")
                .HasColumnName("expired_at");
        });

        builder.OwnsOne(submission => submission.CredentialWindow, owned =>
        {
            owned.Property(window => window.PlanningSummary)
                .HasMaxLength(1000)
                .HasColumnName("credential_window_planning_summary");
            owned.Property(window => window.PlanningApprovedAt)
                .HasColumnType("timestamp with time zone")
                .HasColumnName("credential_window_planning_approved_at");
        });

        builder.OwnsOne(submission => submission.RollbackPlan, owned =>
        {
            owned.Property(plan => plan.RollbackOwner)
                .IsRequired()
                .HasMaxLength(160)
                .HasColumnName("rollback_owner");
            owned.Property(plan => plan.EmergencyOwner)
                .IsRequired()
                .HasMaxLength(160)
                .HasColumnName("emergency_owner");
            owned.Property(plan => plan.RollbackSummary)
                .HasMaxLength(1000)
                .HasColumnName("rollback_summary");
        });

        builder.OwnsOne(submission => submission.EvidenceArchive, owned =>
        {
            owned.Property(archive => archive.EvidenceSummary)
                .HasMaxLength(1000)
                .HasColumnName("evidence_summary");
            owned.Property(archive => archive.ArtifactIds)
                .IsRequired()
                .HasColumnName("evidence_artifact_ids");
        });

        builder.Property(submission => submission.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(submission => submission.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");

        builder.HasIndex(submission => submission.RequestedByUserId);
        builder.HasIndex(submission => submission.Status);
        builder.HasIndex(submission => submission.UpdatedAt);
    }
}
