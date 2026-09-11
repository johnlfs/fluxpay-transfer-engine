namespace FluxPay.Api.Contracts.Accounts;

public sealed record CreateAccountRequest(
    string AccountNumber,
    string OwnerName,
    decimal InitialBalance);
