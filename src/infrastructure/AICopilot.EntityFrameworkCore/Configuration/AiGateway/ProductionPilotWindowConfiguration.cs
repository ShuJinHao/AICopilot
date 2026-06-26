using AICopilot.Core.AiGateway.Aggregates.ProductionOperations;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class ProductionPilotWindowConfiguration : IEntityTypeConfiguration<ProductionPilotWindow>
{
    public void Configure(EntityTypeBuilder<ProductionPilotWindow> builder)
    {
        builder.ToTable("production_pilot_windows");

        builder.HasKey(window => window.Id);
        builder.Property(window => window.Id)
            .HasConversion(id => id.Value, value => new ProductionPilotWindowId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(window => window.WindowId)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("window_id");
        builder.HasIndex(window => window.WindowId).IsUnique();

        builder.Property(window => window.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("name");

        builder.Property(window => window.Status)
            .IsRequired()
            .HasMaxLength(80)
            .HasColumnName("status");

        builder.Property(window => window.StartAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("start_at");

        builder.Property(window => window.EndAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("end_at");

        builder.Property(window => window.AllowedEndpointCodes)
            .IsRequired()
            .HasColumnType("text[]")
            .HasColumnName("allowed_endpoint_codes");

        builder.Property(window => window.MaxTimeRangeDays).HasColumnName("max_time_range_days");
        builder.Property(window => window.MaxRows).HasColumnName("max_rows");
        builder.Property(window => window.TimeoutMs).HasColumnName("timeout_ms");

        builder.Property(window => window.OwnerDepartment)
            .IsRequired()
            .HasMaxLength(120)
            .HasColumnName("owner_department");

        builder.Property(window => window.ApprovalPolicy)
            .IsRequired()
            .HasMaxLength(120)
            .HasColumnName("approval_policy");

        builder.Property(window => window.RollbackPolicy)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("rollback_policy");

        builder.Property(window => window.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(window => window.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");

        builder.HasIndex(window => window.Status);
        builder.HasIndex(window => window.StartAt);
        builder.HasIndex(window => window.EndAt);
    }
}
