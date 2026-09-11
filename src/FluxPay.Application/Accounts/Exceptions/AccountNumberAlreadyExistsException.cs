namespace FluxPay.Application.Accounts.Exceptions;

public sealed class AccountNumberAlreadyExistsException : Exception
{
    public AccountNumberAlreadyExistsException(string accountNumber)
        : this(accountNumber, innerException: null)
    {
    }

    public AccountNumberAlreadyExistsException(
        string accountNumber,
        Exception? innerException)
        : base(
            $"Account number '{accountNumber}' already exists.",
            innerException)
    {
        AccountNumber = accountNumber;
    }

    public string AccountNumber { get; }
}
