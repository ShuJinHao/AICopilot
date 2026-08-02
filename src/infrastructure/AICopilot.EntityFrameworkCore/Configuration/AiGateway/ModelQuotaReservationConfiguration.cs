using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.ModelQuota;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class ModelQuotaReservationConfiguration : IEntityTypeConfiguration<ModelQuotaReservation>
{
    public void Configure(EntityTypeBuilder<ModelQuotaReservation> builder)
    {
        builder.ToTable("model_quota_reservations");
        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Id)
            .HasConversion(id => id.Value, value => new ModelQuotaReservationId(value))
            .HasColumnName("id");
        builder.Property<uint>("RowVersion").IsRowVersion();
        builder.Property(reservation => reservation.TenantKeyHash).HasMaxLength(128).HasColumnName("tenant_key_hash");
        builder.Property(reservation => reservation.UserId).HasColumnName("user_id");
        builder.Property(reservation => reservation.RoleKeyHash).HasMaxLength(128).HasColumnName("role_key_hash");
        builder.Property(reservation => reservation.ModelId)
            .HasConversion(id => id.Value, value => new LanguageModelId(value))
            .HasColumnName("model_id");
        builder.Property(reservation => reservation.EndpointId).HasMaxLength(160).HasColumnName("endpoint_id");
        builder.Property(reservation => reservation.PoolName).HasMaxLength(120).HasColumnName("pool_name");
        builder.Property(reservation => reservation.WindowStartedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("window_started_at_utc");
        builder.Property(reservation => reservation.WindowEndsAtUtc).HasColumnType("timestamp with time zone").HasColumnName("window_ends_at_utc");
        builder.Property(reservation => reservation.EstimatedInputTokens).HasColumnName("estimated_input_tokens");
        builder.Property(reservation => reservation.EstimatedOutputTokens).HasColumnName("estimated_output_tokens");
        builder.Property(reservation => reservation.ActualInputTokens).HasColumnName("actual_input_tokens");
        builder.Property(reservation => reservation.ActualOutputTokens).HasColumnName("actual_output_tokens");
        builder.Property(reservation => reservation.ConcurrencySlots).HasColumnName("concurrency_slots");
        builder.Property(reservation => reservation.FencingToken).HasColumnName("fencing_token");
        builder.Property(reservation => reservation.CorrelationHash).HasMaxLength(128).HasColumnName("correlation_hash");
        builder.Property(reservation => reservation.Status).HasConversion<string>().HasMaxLength(40).HasColumnName("status");
        builder.Property(reservation => reservation.FailureCode).HasMaxLength(120).HasColumnName("failure_code");
        builder.Property(reservation => reservation.ReservedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("reserved_at_utc");
        builder.Property(reservation => reservation.ExpiresAtUtc).HasColumnType("timestamp with time zone").HasColumnName("expires_at_utc");
        builder.Property(reservation => reservation.SettledAtUtc).HasColumnType("timestamp with time zone").HasColumnName("settled_at_utc");

        builder.HasIndex(reservation => reservation.CorrelationHash)
            .IsUnique()
            .HasDatabaseName("ux_model_quota_reservations_correlation");
        builder.HasIndex(reservation => new
            {
                reservation.EndpointId,
                reservation.ModelId,
                reservation.WindowStartedAtUtc,
                reservation.Status
            })
            .HasDatabaseName("ix_model_quota_reservations_endpoint_window");
        builder.HasIndex(reservation => new
            {
                reservation.TenantKeyHash,
                reservation.UserId,
                reservation.RoleKeyHash,
                reservation.WindowStartedAtUtc
            })
            .HasDatabaseName("ix_model_quota_reservations_authority_window");
        builder.HasIndex(reservation => new { reservation.Status, reservation.ExpiresAtUtc })
            .HasDatabaseName("ix_model_quota_reservations_expiry");
    }
}
