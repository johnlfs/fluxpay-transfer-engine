using System.Text.Json;
using FluxPay.Application.Abstractions.Messaging;

namespace FluxPay.Infrastructure.Persistence.Outbox;

public sealed class EfOutboxWriter
    : IOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

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

                OccurredAt =
                    occurredAt.ToUniversalTime(),

                CreatedAt =
                    _timeProvider
                        .GetUtcNow()
                        .ToUniversalTime(),

                PublishedAt =
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
}
