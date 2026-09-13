using System.Diagnostics;
using System.Globalization;
using FluxPay.Application.Abstractions.Messaging;
using FluxPay.Application.Common.Time;
using FluxPay.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.Infrastructure.Persistence.Outbox;

public sealed class OutboxProcessor
{
    private const int MaximumErrorLength =
        4000;

    private static readonly TimeSpan PublishTimeout =
        TimeSpan.FromSeconds(
            10);

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

        var now =
            GetUtcNow();

        var candidateIds =
            await _dbContext.OutboxMessages
                .AsNoTracking()
                .Where(
                    message =>
                        message.PublishedAt == null
                        && message.DeadLetteredAt == null
                        && (
                            message.NextAttemptAt == null
                            || message.NextAttemptAt <= now
                        ))
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

        var deadLettered =
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

                case OutboxMessageProcessingStatus.DeadLettered:
                    deadLettered++;
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
            deadLettered,
            skipped);
    }

    private async Task<OutboxMessageProcessingStatus> ProcessMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var processingStartedAt =
            GetUtcNow();

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
                          AND dead_lettered_at IS NULL
                          AND (
                              next_attempt_at IS NULL
                              OR next_attempt_at <= {processingStartedAt}
                          )
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

            using var publishActivity =
                StartPublishActivity(
                    message);

            try
            {
                using var publishCancellation =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken);

                publishCancellation.CancelAfter(
                    PublishTimeout);

                await _publisher.PublishAsync(
                    message.Id,
                    message.EventType,
                    message.AggregateId,
                    message.Payload,
                    publishCancellation.Token);

                publishActivity?.SetStatus(
                    ActivityStatusCode.Ok);

                message.PublishedAt =
                    GetUtcNow();

                message.NextAttemptAt =
                    null;

                message.DeadLetteredAt =
                    null;

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
                publishActivity?.SetStatus(
                    ActivityStatusCode.Error,
                    exception.Message);

                publishActivity?.SetTag(
                    "error.type",
                    exception
                        .GetType()
                        .FullName);

                message.LastError =
                    FormatError(
                        exception);

                if (
                    message.AttemptCount
                    >= OutboxRetryPolicy.MaximumAttempts)
                {
                    message.NextAttemptAt =
                        null;

                    message.DeadLetteredAt =
                        GetUtcNow();

                    await _dbContext.SaveChangesAsync(
                        cancellationToken);

                    await transaction.CommitAsync(
                        cancellationToken);

                    return OutboxMessageProcessingStatus.DeadLettered;
                }

                message.NextAttemptAt =
                    GetUtcNow()
                    + OutboxRetryPolicy.GetDelay(
                        message.AttemptCount);

                message.DeadLetteredAt =
                    null;

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

    private static Activity? StartPublishActivity(
        OutboxMessage message)
    {
        var parentContext =
            default(
                ActivityContext);

        var hasPersistedParent =
            !string.IsNullOrWhiteSpace(
                message.TraceParent)
            && ActivityContext.TryParse(
                message.TraceParent,
                message.TraceState,
                isRemote:
                    true,
                out parentContext);

        var activityName =
            $"{message.EventType} publish";

        var activity =
            hasPersistedParent
                ? MessagingActivitySource.Source
                    .StartActivity(
                        activityName,
                        ActivityKind.Producer,
                        parentContext)
                : MessagingActivitySource.Source
                    .StartActivity(
                        activityName,
                        ActivityKind.Producer);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag(
            "messaging.system",
            "rabbitmq");

        activity.SetTag(
            "messaging.operation.type",
            "publish");

        activity.SetTag(
            "messaging.message.id",
            message.Id.ToString(
                "D"));

        activity.SetTag(
            "fluxpay.event.type",
            message.EventType);

        activity.SetTag(
            "fluxpay.aggregate.id",
            message.AggregateId.ToString(
                "D"));

        activity.SetTag(
            "fluxpay.outbox.attempt",
            message.AttemptCount);

        if (
            !string.IsNullOrWhiteSpace(
                message.TraceParent)
            && !hasPersistedParent)
        {
            activity.SetTag(
                "fluxpay.trace_context.invalid",
                true);
        }

        return activity;
    }

    private DateTimeOffset GetUtcNow()
    {
        return UtcTimestamp.GetUtcNow(
            _timeProvider);
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

        var enumerator =
            StringInfo.GetTextElementEnumerator(
                value);

        var endIndex =
            0;

        while (enumerator.MoveNext())
        {
            var nextEndIndex =
                enumerator.ElementIndex
                + enumerator
                    .GetTextElement()
                    .Length;

            if (
                nextEndIndex
                > MaximumErrorLength)
            {
                break;
            }

            endIndex =
                nextEndIndex;
        }

        return value[..endIndex];
    }

    private enum OutboxMessageProcessingStatus
    {
        Published = 1,
        Failed = 2,
        DeadLettered = 3,
        Skipped = 4
    }
}
