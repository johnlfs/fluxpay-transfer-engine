using FluxPay.Domain.Transfers;

namespace FluxPay.Application.Transfers.ExecuteTransfer;

public sealed record ExecuteTransferResult(
    Guid Id,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    TransferStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinalizedAt);
