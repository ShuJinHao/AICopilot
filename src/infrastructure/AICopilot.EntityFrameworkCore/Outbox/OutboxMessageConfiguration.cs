using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Outbox;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", "outbox");

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id");

        builder.Property(message => message.EventType)
            .IsRequired()
            .HasMaxLength(1024)
            .HasColumnName("event_type");

        builder.Property(message => message.EventTypeName)
            .IsRequired()
            .HasMaxLength(512)
            .HasColumnName("event_type_name");

        builder.Property(message => message.Payload)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("payload");

        builder.Property(message => message.OccurredOnUtc)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasColumnName("occurred_on_utc");

        builder.Property(message => message.ProcessedOnUtc)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("processed_on_utc");

        builder.Property(message => message.DeadLetteredOnUtc)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("dead_lettered_on_utc");

        builder.Property(message => message.NextAttemptUtc)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("next_attempt_utc");

        builder.Property(message => message.RetryCount)
            .IsRequired()
            .HasColumnName("retry_count");

        builder.Property(message => message.Error)
            .HasMaxLength(4000)
            .HasColumnName("error");

        builder.HasIndex(message => new { message.ProcessedOnUtc, message.DeadLetteredOnUtc, message.NextAttemptUtc });
    }
}
