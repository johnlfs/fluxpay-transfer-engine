namespace FluxPay.Application.Accounts.Exceptions;

public sealed class AccountNumberAlreadyExistsException : Exception
{
    public AccountNumberAlreadyExistsException(string accountNumber)
        : base($"Account number '{accountNumber}' already exists.")
    {
        AccountNumber = accountNumber;
    }

    public string AccountNumber { get; }
}
