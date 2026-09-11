namespace FluxPay.Api.Contracts.Accounts;

public sealed record AccountResponse(
    Guid Id,
    string AccountNumber,
    string OwnerName,
    decimal Balance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
