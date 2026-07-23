using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static AICopilot.EntityFrameworkCore.Configuration.AiGateway.AgentExecutionRuntimeConfigurationMapping;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class ChatRuntimeSettingsConfiguration : IEntityTypeConfiguration<ChatRuntimeSettings>
{
    public void Configure(EntityTypeBuilder<ChatRuntimeSettings> builder)
    {
        ConfigureEntity(builder, "chat_runtime_settings", settings => settings.Id, id => id.Value, value => new ChatRuntimeSettingsId(value));

        builder.Property(settings => settings.RoutingHistoryCount)
            .IsRequired()
            .HasColumnName("routing_history_count");

        builder.Property(settings => settings.AnswerHistoryCount)
            .IsRequired()
            .HasColumnName("answer_history_count");

        builder.Property(settings => settings.RagRewriteHistoryCount)
            .IsRequired()
            .HasColumnName("rag_rewrite_history_count");

        builder.Property(settings => settings.AgentPlanningHistoryCount)
            .IsRequired()
            .HasColumnName("agent_planning_history_count");

        builder.Property(settings => settings.ContextTokenLimit)
            .IsRequired()
            .HasColumnName("context_token_limit");

        builder.Property(settings => settings.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(settings => settings.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");
    }
}
