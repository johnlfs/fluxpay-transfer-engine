namespace FluxPay.Application.Abstractions.Messaging;

public interface IIntegrationEventPublisher
{
    Task PublishAsync(
        Guid messageId,
        string eventType,
        Guid aggregateId,
        string payload,
        CancellationToken cancellationToken = default);
}
