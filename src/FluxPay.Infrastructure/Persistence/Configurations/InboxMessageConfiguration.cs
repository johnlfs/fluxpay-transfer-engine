using FluxPay.Infrastructure.Persistence.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxPay.Infrastructure.Persistence.Configurations;

public sealed class InboxMessageConfiguration
    : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(
        EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable(
            "consumer_inbox_messages",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_consumer_inbox_messages_consumer_name_not_empty",
                    "length(consumer_name) > 0");

                tableBuilder.HasCheckConstraint(
                    "ck_consumer_inbox_messages_event_type_not_empty",
                    "length(event_type) > 0");

                tableBuilder.HasCheckConstraint(
                    "ck_consumer_inbox_messages_processed_after_received",
                    """
                    processed_at IS NULL
                    OR processed_at >= received_at
                    """);
            });

        builder.HasKey(
                message =>
                    new
                    {
                        message.ConsumerName,
                        message.MessageId
                    })
            .HasName(
                "pk_consumer_inbox_messages");

        builder.Property(
                message =>
                    message.ConsumerName)
            .HasColumnName(
                "consumer_name")
            .HasMaxLength(
                200)
            .IsRequired();

        builder.Property(
                message =>
                    message.MessageId)
            .HasColumnName(
                "message_id")
            .ValueGeneratedNever()
            .IsRequired();

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
                    message.ReceivedAt)
            .HasColumnName(
                "received_at")
            .HasColumnType(
                "timestamp with time zone")
            .IsRequired();

        builder.Property(
                message =>
                    message.ProcessedAt)
            .HasColumnName(
                "processed_at")
            .HasColumnType(
                "timestamp with time zone");

        builder.HasIndex(
                message =>
                    message.MessageId)
            .HasDatabaseName(
                "ix_consumer_inbox_messages_message_id");

        builder.HasIndex(
                message =>
                    message.ProcessedAt)
            .HasDatabaseName(
                "ix_consumer_inbox_messages_processed_at");
    }
}
