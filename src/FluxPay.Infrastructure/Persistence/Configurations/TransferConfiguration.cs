using FluxPay.Domain.Accounts;
using FluxPay.Domain.Transfers;
using FluxPay.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxPay.Infrastructure.Persistence.Configurations;

public sealed class TransferConfiguration
    : IEntityTypeConfiguration<Transfer>
{
    public void Configure(
        EntityTypeBuilder<Transfer> builder)
    {
        builder.ToTable(
            "transfers",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_transfers_amount_positive",
                    "amount > 0");

                tableBuilder.HasCheckConstraint(
                    "ck_transfers_different_accounts",
                    "source_account_id <> destination_account_id");

                tableBuilder.HasCheckConstraint(
                    "ck_transfers_valid_status",
                    "status IN ('Pending', 'Completed')");

                tableBuilder.HasCheckConstraint(
                    "ck_transfers_status_consistency",
                    "(status = 'Pending' AND finalized_at IS NULL) " +
                    "OR " +
                    "(status = 'Completed' AND finalized_at IS NOT NULL)");
            });

        builder.HasKey(
            transfer => transfer.Id);

        builder.Property(
                transfer => transfer.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(
                transfer => transfer.SourceAccountId)
            .HasColumnName("source_account_id")
            .IsRequired();

        builder.Property(
                transfer => transfer.DestinationAccountId)
            .HasColumnName("destination_account_id")
            .IsRequired();

        builder.Property(
                transfer => transfer.Amount)
            .HasColumnName("amount")
            .HasConversion(
                money => money.Amount,
                amount => new Money(amount))
            .HasPrecision(19, 2)
            .IsRequired();

        builder.Property(
                transfer => transfer.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(
                transfer => transfer.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(
                transfer => transfer.FinalizedAt)
            .HasColumnName("finalized_at")
            .HasColumnType("timestamp with time zone");


        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(
                transfer => transfer.SourceAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_transfers_source_account");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(
                transfer => transfer.DestinationAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_transfers_destination_account");

        builder.HasIndex(
                transfer => transfer.SourceAccountId)
            .HasDatabaseName(
                "ix_transfers_source_account_id");

        builder.HasIndex(
                transfer => transfer.DestinationAccountId)
            .HasDatabaseName(
                "ix_transfers_destination_account_id");

        builder.HasIndex(
                transfer => transfer.CreatedAt)
            .HasDatabaseName(
                "ix_transfers_created_at");
    }
}
