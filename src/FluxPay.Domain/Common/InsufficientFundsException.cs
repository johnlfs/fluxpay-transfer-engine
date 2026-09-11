namespace FluxPay.Domain.Common;

public sealed class InsufficientFundsException : DomainException
{
    public InsufficientFundsException(decimal availableBalance, decimal requestedAmount)
        : base(
            $"Insufficient funds. Available balance: {availableBalance:0.00}; requested amount: {requestedAmount:0.00}.")
    {
        AvailableBalance = availableBalance;
        RequestedAmount = requestedAmount;
    }

    public decimal AvailableBalance { get; }

    public decimal RequestedAmount { get; }
}
