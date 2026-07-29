using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static AICopilot.EntityFrameworkCore.Configuration.AiGateway.AgentExecutionRuntimeConfigurationMapping;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("approval_requests");

        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id)
            .HasConversion(id => id.Value, value => new ApprovalRequestId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(request => request.TaskId)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .IsRequired()
            .HasColumnName("task_id");

        builder.HasIndex(request => request.TaskId)
            .HasDatabaseName("ix_approval_requests_task_id");

        builder.HasIndex(request => new { request.TaskId, request.ApprovalType })
            .IsUnique()
            .HasFilter("approval_type = 'FinalOutput'")
            .HasDatabaseName("ux_approval_requests_final_output_task");

        builder.Property(request => request.ApprovalType)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("approval_type");

        builder.Property(request => request.TargetId)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("target_id");

        builder.Property(request => request.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("status");

        builder.Property(request => request.RequestedBy)
            .IsRequired()
            .HasColumnName("requested_by");

        builder.Property(request => request.ApprovedBy)
            .HasColumnName("approved_by");

        builder.Property(request => request.ApprovalComment)
            .HasMaxLength(2000)
            .HasColumnName("approval_comment");

        builder.Property(request => request.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(request => request.ApprovedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("approved_at");

        builder.Property(request => request.FinalOutputProofVersion)
            .HasMaxLength(80)
            .HasColumnName("final_output_proof_version");

        builder.Property(request => request.FinalOutputWorkspaceId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ArtifactWorkspaceId(value.Value) : null)
            .HasColumnName("final_output_workspace_id");

        builder.Property(request => request.FinalOutputFinalStepId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentStepId(value.Value) : null)
            .HasColumnName("final_output_final_step_id");

        builder.Property(request => request.FinalOutputRunAttemptId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentTaskRunAttemptId(value.Value) : null)
            .HasColumnName("final_output_run_attempt_id");

        builder.Property(request => request.FinalOutputNodeRunId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentNodeRunId(value.Value) : null)
            .HasColumnName("final_output_node_run_id");

        builder.Property(request => request.FinalOutputTaskFencingToken)
            .HasColumnName("final_output_task_fencing_token");

        builder.Property(request => request.FinalOutputNodeFencingToken)
            .HasColumnName("final_output_node_fencing_token");

        builder.Property(request => request.FinalOutputEvidenceSetDigest)
            .HasMaxLength(64)
            .HasColumnName("final_output_evidence_set_digest");

        builder.Property(request => request.FinalOutputManifestDigest)
            .HasMaxLength(64)
            .HasColumnName("final_output_manifest_digest");

        builder.Property(request => request.FinalOutputArtifactBindingsJson)
            .HasColumnType("jsonb")
            .HasColumnName("final_output_artifact_bindings_json");

        builder.Property(request => request.FinalOutputArtifactBindingDigest)
            .HasMaxLength(64)
            .HasColumnName("final_output_artifact_binding_digest");

        builder.Property(request => request.FinalOutputProofDigest)
            .HasMaxLength(64)
            .HasColumnName("final_output_proof_digest");

        builder.Property(request => request.FinalOutputDecisionProofDigest)
            .HasMaxLength(64)
            .HasColumnName("final_output_decision_proof_digest");
    }
}
