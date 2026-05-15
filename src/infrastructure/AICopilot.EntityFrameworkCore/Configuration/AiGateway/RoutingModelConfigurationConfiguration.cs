using AICopilot.Core.AiGateway.Aggregates.RoutingModel;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class RoutingModelConfigurationConfiguration : IEntityTypeConfiguration<RoutingModelConfiguration>
{
    public void Configure(EntityTypeBuilder<RoutingModelConfiguration> builder)
    {
        builder.ToTable("routing_model_configurations");

        builder.HasKey(configuration => configuration.Id);
        builder.Property(configuration => configuration.Id)
            .HasConversion(id => id.Value, value => new RoutingModelConfigurationId(value))
            .HasColumnName("id");

        builder.Property(configuration => configuration.RowVersion).IsRowVersion();

        builder.Property(configuration => configuration.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("name");

        builder.Property(configuration => configuration.ModelId)
            .HasConversion(id => id.Value, value => new LanguageModelId(value))
            .IsRequired()
            .HasColumnName("model_id");

        builder.Property(configuration => configuration.IsActive)
            .IsRequired()
            .HasColumnName("is_active");

        builder.HasIndex(configuration => configuration.Name).IsUnique();
        builder.HasIndex(configuration => configuration.ModelId);
        builder.HasIndex(configuration => configuration.IsActive)
            .IsUnique()
            .HasFilter("is_active");
    }
}
