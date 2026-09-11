namespace FluxPay.Application.Accounts.GetAccount;

public sealed record GetAccountResult(
    Guid Id,
    string AccountNumber,
    string OwnerName,
    decimal Balance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
