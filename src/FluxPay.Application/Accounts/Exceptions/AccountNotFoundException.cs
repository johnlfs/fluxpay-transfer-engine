namespace FluxPay.Application.Accounts.Exceptions;

public sealed class AccountNotFoundException : Exception
{
    public AccountNotFoundException(Guid accountId)
        : base($"Account '{accountId}' was not found.")
    {
        AccountId = accountId;
    }

    public Guid AccountId { get; }
}
