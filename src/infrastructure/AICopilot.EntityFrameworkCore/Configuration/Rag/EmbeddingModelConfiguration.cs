using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Ids;
using AICopilot.EntityFrameworkCore.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.Rag;

public class EmbeddingModelConfiguration : IEntityTypeConfiguration<EmbeddingModel>
{
    public void Configure(EntityTypeBuilder<EmbeddingModel> builder)
    {
        builder.ToTable("embedding_models");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new EmbeddingModelId(value))
            .HasColumnName("id");

        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("name");

        builder.HasIndex(e => e.Name).IsUnique();

        builder.Property(e => e.Provider)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("provider");

        builder.Property(e => e.BaseUrl)
            .IsRequired()
            .HasMaxLength(500)
            .HasColumnName("base_url");

        builder.Property(e => e.ApiKey)
            .HasConversion<EncryptedStringValueConverter>()
            .HasMaxLength(2048)
            .HasColumnName("api_key");

        builder.Property(e => e.ModelName)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("model_name");

        builder.Property(e => e.Dimensions)
            .IsRequired()
            .HasColumnName("dimensions");

        builder.Property(e => e.MaxTokens)
            .IsRequired()
            .HasColumnName("max_tokens");

        builder.Property(e => e.IsEnabled)
            .IsRequired()
            .HasColumnName("is_enabled");
    }
}
