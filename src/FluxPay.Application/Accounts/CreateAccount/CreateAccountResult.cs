namespace FluxPay.Application.Accounts.CreateAccount;

public sealed record CreateAccountResult(
    Guid Id,
    string AccountNumber,
    string OwnerName,
    decimal Balance,
    DateTimeOffset CreatedAt);
