namespace FluxPay.Application.Transfers.ExecuteTransfer;

public sealed record ExecuteTransferCommand(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount);
