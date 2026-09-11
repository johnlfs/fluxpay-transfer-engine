namespace FluxPay.Application.Abstractions.Persistence;

public enum TransferIdempotencyClaimStatus
{
    Acquired = 1,
    Completed = 2,
    Conflict = 3
}
