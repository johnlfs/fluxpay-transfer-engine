using FluxPay.Application.Abstractions.Messaging;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.Infrastructure.Persistence.Outbox;

public sealed class OutboxProcessor
{
    private const int MaximumErrorLength =
        4000;

    private readonly FluxPayDbContext _dbContext;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly TimeProvider _timeProvider;

    public OutboxProcessor(
        FluxPayDbContext dbContext,
        IIntegrationEventPublisher publisher,
        TimeProvider timeProvider)
    {
        _dbContext =
            dbContext;

        _publisher =
            publisher;

        _timeProvider =
            timeProvider;
    }

    public async Task<OutboxProcessingResult> ProcessBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                batchSize,
                "Outbox batch size must be greater than zero.");
        }

        var candidateIds =
            await _dbContext.OutboxMessages
                .AsNoTracking()
                .Where(
                    message =>
                        message.PublishedAt == null)
                .OrderBy(
                    message =>
                        message.OccurredAt)
                .ThenBy(
                    message =>
                        message.Id)
                .Select(
                    message =>
                        message.Id)
                .Take(
                    batchSize)
                .ToListAsync(
                    cancellationToken);

        var published =
            0;

        var failed =
            0;

        var skipped =
            0;

        foreach (var messageId in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status =
                await ProcessMessageAsync(
                    messageId,
                    cancellationToken);

            switch (status)
            {
                case OutboxMessageProcessingStatus.Published:
                    published++;
                    break;

                case OutboxMessageProcessingStatus.Failed:
                    failed++;
                    break;

                case OutboxMessageProcessingStatus.Skipped:
                    skipped++;
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported outbox processing status '{status}'.");
            }
        }

        return new OutboxProcessingResult(
            candidateIds.Count,
            published,
            failed,
            skipped);
    }

    private async Task<OutboxMessageProcessingStatus> ProcessMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var message =
                await _dbContext.OutboxMessages
                    .FromSqlInterpolated(
                        $"""
                        SELECT *
                        FROM outbox_messages
                        WHERE id = {messageId}
                          AND published_at IS NULL
                        FOR UPDATE SKIP LOCKED
                        """)
                    .SingleOrDefaultAsync(
                        cancellationToken);

            if (message is null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return OutboxMessageProcessingStatus.Skipped;
            }

            message.AttemptCount++;

            try
            {
                await _publisher.PublishAsync(
                    message.Id,
                    message.EventType,
                    message.AggregateId,
                    message.Payload,
                    cancellationToken);

                message.PublishedAt =
                    _timeProvider
                        .GetUtcNow()
                        .ToUniversalTime();

                message.LastError =
                    null;

                await _dbContext.SaveChangesAsync(
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);

                return OutboxMessageProcessingStatus.Published;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                message.LastError =
                    FormatError(
                        exception);

                await _dbContext.SaveChangesAsync(
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);

                return OutboxMessageProcessingStatus.Failed;
            }
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private static string FormatError(
        Exception exception)
    {
        var value =
            $"{exception.GetType().FullName}: {exception.Message}";

        if (value.Length <= MaximumErrorLength)
        {
            return value;
        }

        return value[..MaximumErrorLength];
    }

    private enum OutboxMessageProcessingStatus
    {
        Published = 1,
        Failed = 2,
        Skipped = 3
    }
}
