using AICopilot.EntityFrameworkCore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.Persistence;

public sealed class PersistenceCommitMarkerConfiguration : IEntityTypeConfiguration<PersistenceCommitMarker>
{
    public void Configure(EntityTypeBuilder<PersistenceCommitMarker> builder)
    {
        builder.ToTable("commit_markers", "persistence");

        builder.HasKey(marker => marker.Id);
        builder.Property(marker => marker.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(marker => marker.OperationName)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("operation_name");

        builder.Property(marker => marker.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at_utc");
    }
}
