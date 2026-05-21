using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.DataAnalysis;

public sealed class DataSourcePermissionGrantConfiguration : IEntityTypeConfiguration<DataSourcePermissionGrant>
{
    public void Configure(EntityTypeBuilder<DataSourcePermissionGrant> builder)
    {
        builder.ToTable("data_source_permission_grants");

        builder.HasKey(grant => grant.Id);
        builder.Property(grant => grant.Id)
            .HasConversion(id => id.Value, value => new DataSourcePermissionGrantId(value))
            .HasColumnName("id");

        builder.Property(grant => grant.RowVersion).IsRowVersion();

        builder.Property(grant => grant.DataSourceId)
            .HasConversion(id => id.Value, value => new BusinessDatabaseId(value))
            .IsRequired()
            .HasColumnName("data_source_id");

        builder.Property(grant => grant.TargetType)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("target_type");

        builder.Property(grant => grant.TargetValue)
            .HasMaxLength(160)
            .IsRequired()
            .HasColumnName("target_value");

        builder.Property(grant => grant.CanQuery)
            .IsRequired()
            .HasColumnName("can_query");

        builder.Property(grant => grant.CanSchemaView)
            .IsRequired()
            .HasColumnName("can_schema_view");

        builder.Property(grant => grant.IsEnabled)
            .IsRequired()
            .HasColumnName("is_enabled");

        builder.Property(grant => grant.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(grant => grant.UpdatedAt)
            .IsRequired()
            .HasColumnName("updated_at");

        builder.HasIndex(grant => new { grant.DataSourceId, grant.TargetType, grant.TargetValue })
            .IsUnique()
            .HasDatabaseName("ix_data_source_permission_grants_target");

        builder.HasIndex(grant => grant.DataSourceId)
            .HasDatabaseName("ix_data_source_permission_grants_data_source_id");
    }
}
