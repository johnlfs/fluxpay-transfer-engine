namespace FluxPay.Application.Abstractions.Persistence;

public interface ITransferIdempotencyRepository
{
    Task<TransferIdempotencyClaimResult> TryClaimAsync(
        Guid idempotencyKey,
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal amount,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        Guid idempotencyKey,
        Guid transferId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
}
