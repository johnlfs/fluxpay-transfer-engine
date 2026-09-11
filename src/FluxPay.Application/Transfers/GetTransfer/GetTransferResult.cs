using FluxPay.Domain.Transfers;

namespace FluxPay.Application.Transfers.GetTransfer;

public sealed record GetTransferResult(
    Guid Id,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    TransferStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinalizedAt);
