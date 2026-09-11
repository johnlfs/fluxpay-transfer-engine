namespace FluxPay.Application.Abstractions.Messaging;

public interface IOutboxWriter
{
    Task AddAsync<TPayload>(
        Guid eventId,
        string eventType,
        Guid aggregateId,
        TPayload payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
        where TPayload : class;
}
