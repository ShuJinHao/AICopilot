using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

internal sealed class AgentSessionStateConfiguration : IEntityTypeConfiguration<AgentSessionState>
{
    public void Configure(EntityTypeBuilder<AgentSessionState> builder)
    {
        builder.ToTable("agent_session_states");
        builder.HasKey(state => state.SessionId);
        builder.Property(state => state.SessionId)
            .HasConversion(id => id.Value, value => new SessionId(value))
            .HasColumnName("session_id");
        builder.Property(state => state.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(state => state.TenantId).HasColumnName("tenant_id").HasMaxLength(256);
        builder.Property(state => state.AgentSchemaVersion)
            .HasColumnName("agent_schema_version")
            .IsRequired();
        builder.Property(state => state.ProtectedState)
            .HasColumnName("protected_state")
            .IsRequired();
        builder.Property(state => state.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(state => state.ActiveTurnId).HasColumnName("active_turn_id");
        builder.Property(state => state.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();
        builder.Property(state => state.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(state => state.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(state => state.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(state => state.ProtectedApprovalBindings)
            .HasColumnName("protected_approval_bindings");

        builder.HasIndex(state => new { state.UserId, state.ExpiresAtUtc })
            .HasDatabaseName("ix_agent_session_states_user_expiry");
        builder.HasOne<AICopilot.Core.AiGateway.Aggregates.Sessions.Session>()
            .WithOne()
            .HasForeignKey<AgentSessionState>(state => state.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
