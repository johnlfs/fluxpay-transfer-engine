namespace FluxPay.Application.Abstractions.Persistence;

public sealed record TransferIdempotencyClaimResult(
    TransferIdempotencyClaimStatus Status,
    Guid? TransferId);
