using FluxPay.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxPay.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration
    : IEntityTypeConfiguration<OutboxMessage>
{
    private const int MaximumTraceContextLength =
        512;

    public void Configure(
        EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable(
            "outbox_messages",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_outbox_messages_attempt_count_non_negative",
                    "attempt_count >= 0");

                tableBuilder.HasCheckConstraint(
                    "ck_outbox_messages_event_type_not_empty",
                    "length(event_type) > 0");

                tableBuilder.HasCheckConstraint(
                    "ck_outbox_messages_payload_not_empty",
                    "length(payload::text) > 0");

                tableBuilder.HasCheckConstraint(
                    "ck_outbox_messages_terminal_state",
                    """
                    NOT (
                        published_at IS NOT NULL
                        AND dead_lettered_at IS NOT NULL
                    )
                    """);

                tableBuilder.HasCheckConstraint(
                    "ck_outbox_messages_next_attempt_state",
                    """
                    next_attempt_at IS NULL
                    OR (
                        published_at IS NULL
                        AND dead_lettered_at IS NULL
                    )
                    """);
            });

        builder.HasKey(
                message =>
                    message.Id)
            .HasName(
                "pk_outbox_messages");

        builder.Property(
                message =>
                    message.Id)
            .HasColumnName(
                "id")
            .ValueGeneratedNever();

        builder.Property(
                message =>
                    message.EventType)
            .HasColumnName(
                "event_type")
            .HasMaxLength(
                200)
            .IsRequired();

        builder.Property(
                message =>
                    message.AggregateId)
            .HasColumnName(
                "aggregate_id")
            .IsRequired();

        builder.Property(
                message =>
                    message.Payload)
            .HasColumnName(
                "payload")
            .HasColumnType(
                "jsonb")
            .IsRequired();

        builder.Property(
                message =>
                    message.TraceParent)
            .HasColumnName(
                "trace_parent")
            .HasMaxLength(
                MaximumTraceContextLength);

        builder.Property(
                message =>
                    message.TraceState)
            .HasColumnName(
                "trace_state")
            .HasMaxLength(
                MaximumTraceContextLength);

        builder.Property(
                message =>
                    message.OccurredAt)
            .HasColumnName(
                "occurred_at")
            .HasColumnType(
                "timestamp with time zone")
            .IsRequired();

        builder.Property(
                message =>
                    message.CreatedAt)
            .HasColumnName(
                "created_at")
            .HasColumnType(
                "timestamp with time zone")
            .IsRequired();

        builder.Property(
                message =>
                    message.PublishedAt)
            .HasColumnName(
                "published_at")
            .HasColumnType(
                "timestamp with time zone");

        builder.Property(
                message =>
                    message.NextAttemptAt)
            .HasColumnName(
                "next_attempt_at")
            .HasColumnType(
                "timestamp with time zone");

        builder.Property(
                message =>
                    message.DeadLetteredAt)
            .HasColumnName(
                "dead_lettered_at")
            .HasColumnType(
                "timestamp with time zone");

        builder.Property(
                message =>
                    message.AttemptCount)
            .HasColumnName(
                "attempt_count")
            .HasDefaultValue(
                0)
            .IsRequired();

        builder.Property(
                message =>
                    message.LastError)
            .HasColumnName(
                "last_error")
            .HasColumnType(
                "text");

        builder.HasIndex(
                message =>
                    message.AggregateId)
            .HasDatabaseName(
                "ix_outbox_messages_aggregate_id");

        builder.HasIndex(
                message =>
                    new
                    {
                        message.NextAttemptAt,
                        message.OccurredAt,
                        message.Id
                    })
            .HasDatabaseName(
                "ix_outbox_messages_dispatchable")
            .HasFilter(
                "published_at IS NULL AND dead_lettered_at IS NULL");

        builder.HasIndex(
                message =>
                    message.PublishedAt)
            .HasDatabaseName(
                "ix_outbox_messages_published_at");
    }
}
