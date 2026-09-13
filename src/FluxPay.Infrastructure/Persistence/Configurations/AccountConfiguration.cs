using FluxPay.Domain.Accounts;
using FluxPay.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxPay.Infrastructure.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable(
            "accounts",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_accounts_balance_non_negative",
                    "balance >= 0");
            });

        builder.HasKey(account => account.Id);

        builder.Property(account => account.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(account => account.AccountNumber)
            .HasColumnName("account_number")
            .HasMaxLength(
                Account.MaximumAccountNumberLength)
            .IsRequired();

        builder.HasIndex(account => account.AccountNumber)
            .IsUnique()
            .HasDatabaseName(
                AccountDatabaseNames.AccountNumberUniqueIndex);

        builder.Property(account => account.OwnerName)
            .HasColumnName("owner_name")
            .HasMaxLength(
                Account.MaximumOwnerNameLength)
            .IsRequired();

        builder.Property(account => account.Balance)
            .HasColumnName("balance")
            .HasConversion(
                money => money.Amount,
                amount => new Money(amount))
            .HasPrecision(19, 2)
            .IsRequired();

        builder.Property(account => account.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(account => account.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}
