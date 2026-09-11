using FluxPay.Domain.Common;
using FluxPay.Domain.ValueObjects;

namespace FluxPay.Domain.Accounts;

public sealed class Account
{
    private Account(
        Guid id,
        string accountNumber,
        string ownerName,
        Money balance,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountNumber = accountNumber;
        OwnerName = ownerName;
        Balance = balance;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; }

    public string AccountNumber { get; }

    public string OwnerName { get; }

    public Money Balance { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Account Create(
        string accountNumber,
        string ownerName,
        Money initialBalance,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            throw new DomainValidationException(
                "Account number is required.");
        }

        if (string.IsNullOrWhiteSpace(ownerName))
        {
            throw new DomainValidationException(
                "Account owner name is required.");
        }

        return new Account(
            Guid.NewGuid(),
            accountNumber.Trim(),
            ownerName.Trim(),
            initialBalance,
            createdAt.ToUniversalTime());
    }

    public void Debit(Money amount, DateTimeOffset occurredAt)
    {
        EnsurePositiveTransactionAmount(amount);

        if (Balance.Amount < amount.Amount)
        {
            throw new InsufficientFundsException(
                Balance.Amount,
                amount.Amount);
        }

        Balance = new Money(Balance.Amount - amount.Amount);
        UpdatedAt = occurredAt.ToUniversalTime();
    }

    public void Credit(Money amount, DateTimeOffset occurredAt)
    {
        EnsurePositiveTransactionAmount(amount);

        Balance = new Money(Balance.Amount + amount.Amount);
        UpdatedAt = occurredAt.ToUniversalTime();
    }

    private static void EnsurePositiveTransactionAmount(Money amount)
    {
        if (amount.Amount <= 0)
        {
            throw new DomainValidationException(
                "Transaction amount must be greater than zero.");
        }
    }
}
