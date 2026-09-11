using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Infrastructure.Persistence.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.Infrastructure.Persistence.Repositories;

public sealed class EfTransferIdempotencyRepository
    : ITransferIdempotencyRepository
{
    private readonly FluxPayDbContext _dbContext;

    public EfTransferIdempotencyRepository(
        FluxPayDbContext dbContext)
    {
        _dbContext =
            dbContext;
    }

    public async Task<TransferIdempotencyClaimResult> TryClaimAsync(
        Guid idempotencyKey,
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal amount,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Idempotency claims require an active database transaction.");
        }

        var affectedRows =
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO transfer_idempotency
                (
                    idempotency_key,
                    source_account_id,
                    destination_account_id,
                    amount,
                    transfer_id,
                    created_at,
                    completed_at
                )
                VALUES
                (
                    {idempotencyKey},
                    {sourceAccountId},
                    {destinationAccountId},
                    {amount},
                    NULL,
                    {createdAt.ToUniversalTime()},
                    NULL
                )
                ON CONFLICT (idempotency_key)
                DO NOTHING
                """,
                cancellationToken);

        if (affectedRows == 1)
        {
            return new TransferIdempotencyClaimResult(
                TransferIdempotencyClaimStatus.Acquired,
                TransferId: null);
        }

        if (affectedRows != 0)
        {
            throw new InvalidOperationException(
                $"Unexpected affected row count while claiming idempotency key: {affectedRows}.");
        }

        var existingRecord =
            await _dbContext
                .Set<TransferIdempotencyRecord>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    record =>
                        record.IdempotencyKey
                            == idempotencyKey,
                    cancellationToken);

        if (existingRecord is null)
        {
            throw new InvalidOperationException(
                $"Idempotency record '{idempotencyKey}' was not found after a claim conflict.");
        }

        var samePayload =
            existingRecord.SourceAccountId
                == sourceAccountId
            && existingRecord.DestinationAccountId
                == destinationAccountId
            && existingRecord.Amount
                == amount;

        if (!samePayload)
        {
            return new TransferIdempotencyClaimResult(
                TransferIdempotencyClaimStatus.Conflict,
                existingRecord.TransferId);
        }

        if (
            existingRecord.TransferId is null
            || existingRecord.CompletedAt is null)
        {
            throw new InvalidOperationException(
                $"Idempotency record '{idempotencyKey}' exists but is incomplete.");
        }

        return new TransferIdempotencyClaimResult(
            TransferIdempotencyClaimStatus.Completed,
            existingRecord.TransferId);
    }

    public async Task CompleteAsync(
        Guid idempotencyKey,
        Guid transferId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Completing an idempotency record requires an active database transaction.");
        }

        var affectedRows =
            await _dbContext
                .Set<TransferIdempotencyRecord>()
                .Where(
                    record =>
                        record.IdempotencyKey
                            == idempotencyKey
                        && record.TransferId
                            == null
                        && record.CompletedAt
                            == null)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                record =>
                                    record.TransferId,
                                (Guid?)transferId)
                            .SetProperty(
                                record =>
                                    record.CompletedAt,
                                (DateTimeOffset?)
                                    completedAt.ToUniversalTime()),
                    cancellationToken);

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Expected to complete one idempotency record for key '{idempotencyKey}', but updated {affectedRows}.");
        }
    }
}
