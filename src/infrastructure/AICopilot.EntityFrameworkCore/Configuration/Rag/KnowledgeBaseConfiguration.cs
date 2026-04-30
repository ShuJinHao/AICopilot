using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;

namespace AICopilot.EntityFrameworkCore.Configuration.Rag;

public class KnowledgeBaseConfiguration : IEntityTypeConfiguration<KnowledgeBase>
{
    public void Configure(EntityTypeBuilder<KnowledgeBase> builder)
    {
        builder.ToTable("knowledge_bases");

        builder.HasKey(kb => kb.Id);
        builder.Property(kb => kb.Id)
            .HasConversion(id => id.Value, value => new KnowledgeBaseId(value))
            .HasColumnName("id");

        builder.Property(kb => kb.RowVersion).IsRowVersion();

        builder.Property(kb => kb.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("name");

        builder.Property(kb => kb.Description)
            .HasMaxLength(1000)
            .HasColumnName("description");

        builder.Property(kb => kb.EmbeddingModelId)
            .HasConversion(id => id.Value, value => new EmbeddingModelId(value))
            .IsRequired()
            .HasColumnName("embedding_model_id");
        
        // 配置导航属性 Documents
        builder.HasMany(kb => kb.Documents)
            .WithOne(d => d.KnowledgeBase)
            .HasForeignKey(d => d.KnowledgeBaseId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade); // 删除知识库时级联删除文档
    }
}
