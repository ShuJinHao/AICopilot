using AICopilot.Core.AiGateway.Aggregates.ProductionOperations;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class ProductionPilotIncidentConfiguration : IEntityTypeConfiguration<ProductionPilotIncident>
{
    public void Configure(EntityTypeBuilder<ProductionPilotIncident> builder)
    {
        builder.ToTable("production_pilot_incidents");

        builder.HasKey(incident => incident.Id);
        builder.Property(incident => incident.Id)
            .HasConversion(id => id.Value, value => new ProductionPilotIncidentId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(incident => incident.Severity)
            .IsRequired()
            .HasMaxLength(40)
            .HasColumnName("severity");

        builder.Property(incident => incident.Category)
            .IsRequired()
            .HasMaxLength(120)
            .HasColumnName("category");

        builder.Property(incident => incident.Status)
            .IsRequired()
            .HasMaxLength(40)
            .HasColumnName("status");

        builder.Property(incident => incident.Owner)
            .HasMaxLength(120)
            .HasColumnName("owner");

        builder.Property(incident => incident.SourceRef)
            .HasMaxLength(240)
            .HasColumnName("source_ref");

        builder.Property(incident => incident.ResolutionHash)
            .HasMaxLength(128)
            .HasColumnName("resolution_hash");

        builder.Property(incident => incident.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(incident => incident.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");

        builder.HasIndex(incident => incident.Status);
        builder.HasIndex(incident => incident.Severity);
        builder.HasIndex(incident => incident.UpdatedAt);
    }
}
