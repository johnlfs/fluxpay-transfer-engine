using FluxPay.Domain.Common;

namespace FluxPay.Domain.ValueObjects;

public readonly record struct Money
{
    public const decimal MaximumAmount =
        99_999_999_999_999_999.99m;

    public Money(decimal amount)
    {
        if (amount < 0)
        {
            throw new DomainValidationException("Money amount cannot be negative.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainValidationException(
                "Money amount cannot have more than two decimal places.");
        }

        if (amount > MaximumAmount)
        {
            throw new DomainValidationException(
                "Money amount exceeds the supported numeric(19,2) range.");
        }

        Amount = amount;
    }

    public decimal Amount { get; }

    public static Money Zero => new(0m);

    public override string ToString()
    {
        return Amount.ToString("0.00");
    }
}
