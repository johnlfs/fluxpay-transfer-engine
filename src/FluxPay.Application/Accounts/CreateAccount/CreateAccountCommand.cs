namespace FluxPay.Application.Accounts.CreateAccount;

public sealed record CreateAccountCommand(
    string AccountNumber,
    string OwnerName,
    decimal InitialBalance);
