using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;

namespace AICopilot.EntityFrameworkCore.Configuration.Rag;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasConversion(id => id.Value, value => new DocumentId(value))
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(d => d.KnowledgeBaseId)
            .HasConversion(id => id.Value, value => new KnowledgeBaseId(value))
            .IsRequired()
            .HasColumnName("knowledge_base_id");

        builder.Property(d => d.Name)
            .IsRequired()
            .HasMaxLength(256)
            .HasColumnName("name");

        builder.Property(d => d.FilePath)
            .IsRequired()
            .HasMaxLength(500)
            .HasColumnName("file_path");

        builder.Property(d => d.Extension)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("extension");

        builder.Property(d => d.FileHash)
            .IsRequired()
            .HasMaxLength(64) // 使用 SHA256，通常为 64 字符
            .HasColumnName("file_hash");

        // 状态枚举：建议存为字符串，方便数据库直观查看
        builder.Property(d => d.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>() 
            .HasColumnName("status");

        builder.Property(d => d.ChunkCount)
            .IsRequired()
            .HasColumnName("chunk_count");

        builder.Property(d => d.ErrorMessage)
            .HasColumnName("error_message"); // 允许为空

        builder.Property(d => d.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(d => d.ProcessedAt)
            .HasColumnName("processed_at"); // 允许为空

        builder.Property(d => d.Classification)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>()
            .HasDefaultValue(DocumentClassification.Internal)
            .HasColumnName("classification");

        builder.Property(d => d.SourceType)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>()
            .HasDefaultValue(DocumentSourceType.UserUploaded)
            .HasColumnName("source_type");

        builder.Property(d => d.IsSanitized)
            .IsRequired()
            .HasDefaultValue(false)
            .HasColumnName("is_sanitized");

        builder.Property(d => d.ReviewedBy)
            .HasMaxLength(100)
            .HasColumnName("reviewed_by");

        builder.Property(d => d.ReviewedAt)
            .HasColumnName("reviewed_at");

        builder.Property(d => d.EffectiveFrom)
            .HasColumnName("effective_from");

        builder.Property(d => d.EffectiveTo)
            .HasColumnName("effective_to");

        builder.Property(d => d.AllowedForFinalPrompt)
            .IsRequired()
            .HasDefaultValue(true)
            .HasColumnName("allowed_for_final_prompt");

        builder.Property(d => d.BlockedReason)
            .HasMaxLength(500)
            .HasColumnName("blocked_reason");

        builder.Property(d => d.CategoryId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new KnowledgeCategoryId(value.Value) : null)
            .HasColumnName("category_id");

        builder.Property(d => d.DocumentGroupId)
            .IsRequired()
            .HasDefaultValueSql("gen_random_uuid()")
            .HasColumnName("document_group_id");

        builder.Property(d => d.VersionNo)
            .IsRequired()
            .HasDefaultValue(1)
            .HasColumnName("version_no");

        builder.Property(d => d.EffectiveAt)
            .HasColumnName("effective_at");

        builder.Property(d => d.ExpiredAt)
            .HasColumnName("expired_at");

        builder.Property(d => d.SupersededByDocumentId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (int?)null,
                value => value.HasValue ? new DocumentId(value.Value) : null)
            .HasColumnName("superseded_by_document_id");

        builder.HasIndex(d => new { d.DocumentGroupId, d.VersionNo })
            .HasDatabaseName("ix_documents_group_version");

        builder.HasIndex(d => d.Status)
            .HasDatabaseName("ix_documents_status");
        
        // 配置导航属性 Chunks
        builder.HasMany(d => d.Chunks)
            .WithOne(c => c.Document)
            .HasForeignKey(c => c.DocumentId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade); // 删除文档时级联删除切片
    }
}
