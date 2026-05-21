using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.Rag;

public sealed class KnowledgeCategoryConfiguration : IEntityTypeConfiguration<KnowledgeCategory>
{
    public void Configure(EntityTypeBuilder<KnowledgeCategory> builder)
    {
        builder.ToTable("knowledge_categories");

        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id)
            .HasConversion(id => id.Value, value => new KnowledgeCategoryId(value))
            .HasColumnName("id");

        builder.Property(category => category.RowVersion).IsRowVersion();

        builder.Property(category => category.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("name");

        builder.Property(category => category.BusinessDomain)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("business_domain");

        builder.Property(category => category.Visibility)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("visibility");

        builder.Property(category => category.Department)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("department");

        builder.Property(category => category.Priority)
            .IsRequired()
            .HasColumnName("priority");

        builder.Property(category => category.IsEnabled)
            .IsRequired()
            .HasColumnName("is_enabled");

        builder.Property(category => category.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.HasIndex(category => category.Name)
            .IsUnique()
            .HasDatabaseName("ix_knowledge_categories_name");
    }
}
