namespace FluxPay.Application.Transfers.ExecuteTransfer;

public sealed record ExecuteTransferCommand(
    Guid IdempotencyKey,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount);
