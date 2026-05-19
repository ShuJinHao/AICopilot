using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public class LanguageModelConfiguration : IEntityTypeConfiguration<LanguageModel>
{
    public void Configure(EntityTypeBuilder<LanguageModel> builder)
    {
        builder.ToTable("language_models");

        builder.HasKey(lm => lm.Id);
        builder.Property(lm => lm.Id)
            .HasConversion(id => id.Value, value => new LanguageModelId(value))
            .HasColumnName("id");

        builder.Property(lm => lm.RowVersion).IsRowVersion();

        builder.Property(lm => lm.Provider)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("provider");

        builder.Property(lm => lm.ProtocolType)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("protocol_type");

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
            .HasMaxLength(2048)
            .HasColumnName("api_key");

        builder.Property(lm => lm.Usage)
            .IsRequired()
            .HasConversion<int>()
            .HasColumnName("usage");

        builder.Property(lm => lm.IsEnabled)
            .IsRequired()
            .HasColumnName("is_enabled");

        builder.Property(lm => lm.ConnectivityStatus)
            .IsRequired()
            .HasConversion<int>()
            .HasColumnName("connectivity_status");

        builder.Property(lm => lm.ConnectivityCheckedAt)
            .HasColumnName("connectivity_checked_at");

        builder.Property(lm => lm.ConnectivityError)
            .HasMaxLength(1000)
            .HasColumnName("connectivity_error");

        builder.OwnsOne(lm => lm.Parameters, parametersBuilder =>
        {
            parametersBuilder.Property(p => p.MaxTokens)
                .IsRequired()
                .HasColumnName("max_tokens");

            parametersBuilder.Property(p => p.MaxOutputTokens)
                .IsRequired()
                .HasColumnName("max_output_tokens");

            parametersBuilder.Property(p => p.Temperature)
                .IsRequired()
                .HasColumnName("temperature");
        });
    }
}
