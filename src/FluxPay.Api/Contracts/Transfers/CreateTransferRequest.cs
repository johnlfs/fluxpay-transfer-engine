namespace FluxPay.Api.Contracts.Transfers;

public sealed record CreateTransferRequest(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount);
