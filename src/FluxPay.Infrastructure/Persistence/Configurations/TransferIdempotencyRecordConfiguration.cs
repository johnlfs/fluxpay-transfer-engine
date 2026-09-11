using FluxPay.Domain.Transfers;
using FluxPay.Infrastructure.Persistence.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxPay.Infrastructure.Persistence.Configurations;

public sealed class TransferIdempotencyRecordConfiguration
    : IEntityTypeConfiguration<TransferIdempotencyRecord>
{
    public void Configure(
        EntityTypeBuilder<TransferIdempotencyRecord> builder)
    {
        builder.ToTable(
            "transfer_idempotency",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_transfer_idempotency_amount_positive",
                    "amount > 0");

                tableBuilder.HasCheckConstraint(
                    "ck_transfer_idempotency_different_accounts",
                    "source_account_id <> destination_account_id");

                tableBuilder.HasCheckConstraint(
                    "ck_transfer_idempotency_completion_consistency",
                    "(" +
                    "transfer_id IS NULL " +
                    "AND completed_at IS NULL" +
                    ") OR (" +
                    "transfer_id IS NOT NULL " +
                    "AND completed_at IS NOT NULL" +
                    ")");
            });

        builder.HasKey(
                record =>
                    record.IdempotencyKey)
            .HasName(
                "pk_transfer_idempotency");

        builder.Property(
                record =>
                    record.IdempotencyKey)
            .HasColumnName(
                "idempotency_key")
            .ValueGeneratedNever();

        builder.Property(
                record =>
                    record.SourceAccountId)
            .HasColumnName(
                "source_account_id")
            .IsRequired();

        builder.Property(
                record =>
                    record.DestinationAccountId)
            .HasColumnName(
                "destination_account_id")
            .IsRequired();

        builder.Property(
                record =>
                    record.Amount)
            .HasColumnName(
                "amount")
            .HasPrecision(
                19,
                2)
            .IsRequired();

        builder.Property(
                record =>
                    record.TransferId)
            .HasColumnName(
                "transfer_id");

        builder.Property(
                record =>
                    record.CreatedAt)
            .HasColumnName(
                "created_at")
            .HasColumnType(
                "timestamp with time zone")
            .IsRequired();

        builder.Property(
                record =>
                    record.CompletedAt)
            .HasColumnName(
                "completed_at")
            .HasColumnType(
                "timestamp with time zone");

        builder.HasOne<Transfer>()
            .WithMany()
            .HasForeignKey(
                record =>
                    record.TransferId)
            .OnDelete(
                DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_transfer_idempotency_transfer");

        builder.HasIndex(
                record =>
                    record.TransferId)
            .IsUnique()
            .HasDatabaseName(
                "ux_transfer_idempotency_transfer_id");

        builder.HasIndex(
                record =>
                    record.CreatedAt)
            .HasDatabaseName(
                "ix_transfer_idempotency_created_at");
    }
}
