using System.Diagnostics;
using System.Text.Json;
using FluxPay.Application.Abstractions.Messaging;
using FluxPay.Application.Common.Time;

namespace FluxPay.Infrastructure.Persistence.Outbox;

public sealed class EfOutboxWriter
    : IOutboxWriter
{
    private const int MaximumTraceStateLength =
        512;

    private static readonly JsonSerializerOptions SerializerOptions =
        new(
            JsonSerializerDefaults.Web);

    private readonly FluxPayDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public EfOutboxWriter(
        FluxPayDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext =
            dbContext;

        _timeProvider =
            timeProvider;
    }

    public Task AddAsync<TPayload>(
        Guid eventId,
        string eventType,
        Guid aggregateId,
        TPayload payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
        where TPayload : class
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Outbox messages must be created inside an active database transaction.");
        }

        if (eventId == Guid.Empty)
        {
            throw new ArgumentException(
                "Outbox event id cannot be empty.",
                nameof(eventId));
        }

        if (aggregateId == Guid.Empty)
        {
            throw new ArgumentException(
                "Outbox aggregate id cannot be empty.",
                nameof(aggregateId));
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException(
                "Outbox event type is required.",
                nameof(eventType));
        }

        ArgumentNullException.ThrowIfNull(
            payload);

        var traceContext =
            CaptureTraceContext();

        var message =
            new OutboxMessage
            {
                Id =
                    eventId,

                EventType =
                    eventType,

                AggregateId =
                    aggregateId,

                Payload =
                    JsonSerializer.Serialize(
                        payload,
                        SerializerOptions),

                TraceParent =
                    traceContext.TraceParent,

                TraceState =
                    traceContext.TraceState,

                OccurredAt =
                    UtcTimestamp.Normalize(
                        occurredAt),

                CreatedAt =
                    UtcTimestamp.GetUtcNow(
                        _timeProvider),

                PublishedAt =
                    null,

                NextAttemptAt =
                    null,

                DeadLetteredAt =
                    null,

                AttemptCount =
                    0,

                LastError =
                    null
            };

        return _dbContext
            .Set<OutboxMessage>()
            .AddAsync(
                message,
                cancellationToken)
            .AsTask();
    }

    private static TraceContextSnapshot CaptureTraceContext()
    {
        var activity =
            Activity.Current;

        if (
            activity is null
            || activity.IdFormat
                != ActivityIdFormat.W3C
            || string.IsNullOrWhiteSpace(
                activity.Id))
        {
            return TraceContextSnapshot.Empty;
        }

        var currentTraceState =
            activity.TraceStateString;

        var traceState =
            string.IsNullOrWhiteSpace(
                currentTraceState)
            || currentTraceState.Length
                > MaximumTraceStateLength
                ? null
                : currentTraceState;

        return new TraceContextSnapshot(
            activity.Id,
            traceState);
    }

    private sealed record TraceContextSnapshot(
        string? TraceParent,
        string? TraceState)
    {
        public static TraceContextSnapshot Empty { get; } =
            new(
                null,
                null);
    }
}
