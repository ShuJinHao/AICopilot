using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.EntityFrameworkCore.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public class LanguageModelConfiguration : IEntityTypeConfiguration<LanguageModel>
{
    public void Configure(EntityTypeBuilder<LanguageModel> builder)
    {
        builder.ToTable("language_models");

        builder.HasKey(lm => lm.Id);
        builder.Property(lm => lm.Id).HasColumnName("id");

        builder.Property(lm => lm.RowVersion).IsRowVersion();

        builder.Property(lm => lm.Provider)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("provider");

        builder.Property(lm => lm.Name)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("name");

        builder.HasIndex(lm => new { lm.Provider, lm.Name })
            .IsUnique();

        builder.Property(lm => lm.BaseUrl)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("base_url");

        builder.Property(lm => lm.ApiKey)
            .HasConversion<EncryptedStringValueConverter>()
            .HasMaxLength(2048)
            .HasColumnName("api_key");

        builder.OwnsOne(lm => lm.Parameters, parametersBuilder =>
        {
            parametersBuilder.Property(p => p.MaxTokens)
                .IsRequired()
                .HasColumnName("max_tokens");

            parametersBuilder.Property(p => p.Temperature)
                .IsRequired()
                .HasColumnName("temperature");
        });
    }
}
