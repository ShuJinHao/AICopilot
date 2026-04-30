using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.DataAnalysis;

public class BusinessDatabaseConfiguration : IEntityTypeConfiguration<BusinessDatabase>
{
    public void Configure(EntityTypeBuilder<BusinessDatabase> builder)
    {
        builder.ToTable("business_databases");

        builder.HasKey(db => db.Id);
        builder.Property(db => db.Id).HasColumnName("id");

        builder.Property(db => db.RowVersion).IsRowVersion();

        builder.Property(db => db.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("name");

        builder.HasIndex(db => db.Name)
            .IsUnique();

        builder.Property(db => db.Description)
            .HasMaxLength(1000)
            .HasColumnName("description");

        builder.Property(db => db.ConnectionString)
            .IsRequired()
            .HasColumnName("connection_string");

        builder.Property(db => db.Provider)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("provider");

        builder.Property(db => db.IsReadOnly)
            .IsRequired()
            .HasColumnName("is_read_only");

        builder.Property(db => db.IsEnabled)
            .IsRequired()
            .HasColumnName("is_enabled");

        builder.Property(db => db.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");
    }
}
