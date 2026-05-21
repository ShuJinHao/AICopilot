using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.Rag;

public sealed class KnowledgeSupplementConfiguration : IEntityTypeConfiguration<KnowledgeSupplement>
{
    public void Configure(EntityTypeBuilder<KnowledgeSupplement> builder)
    {
        builder.ToTable("knowledge_supplements");

        builder.HasKey(supplement => supplement.Id);
        builder.Property(supplement => supplement.Id)
            .HasConversion(id => id.Value, value => new KnowledgeSupplementId(value))
            .HasColumnName("id");

        builder.Property(supplement => supplement.RowVersion).IsRowVersion();

        builder.Property(supplement => supplement.Title)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("title");

        builder.Property(supplement => supplement.Content)
            .IsRequired()
            .HasColumnName("content");

        builder.Property(supplement => supplement.Priority)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>()
            .HasColumnName("priority");

        builder.Property(supplement => supplement.EffectiveAt)
            .HasColumnName("effective_at");

        builder.Property(supplement => supplement.ExpiredAt)
            .HasColumnName("expired_at");

        builder.Property(supplement => supplement.CategoryId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new KnowledgeCategoryId(value.Value) : null)
            .HasColumnName("category_id");

        builder.Property(supplement => supplement.DocumentId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (int?)null,
                value => value.HasValue ? new DocumentId(value.Value) : null)
            .HasColumnName("document_id");

        builder.Property(supplement => supplement.IsEnabled)
            .IsRequired()
            .HasColumnName("is_enabled");

        builder.Property(supplement => supplement.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.HasIndex(supplement => new { supplement.CategoryId, supplement.Priority })
            .HasDatabaseName("ix_knowledge_supplements_category_priority");
    }
}
