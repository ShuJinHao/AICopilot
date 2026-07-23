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
    }
}
