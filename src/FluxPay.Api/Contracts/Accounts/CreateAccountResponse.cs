namespace FluxPay.Api.Contracts.Accounts;

public sealed record CreateAccountResponse(
    Guid Id,
    string AccountNumber,
    string OwnerName,
    decimal Balance,
    DateTimeOffset CreatedAt);
