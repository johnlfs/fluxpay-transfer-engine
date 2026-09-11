namespace FluxPay.Application.Abstractions.Persistence;

public interface IInboxRepository
{
    Task<bool> TryClaimAsync(
        string consumerName,
        Guid messageId,
        string eventType,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken = default);

    Task MarkProcessedAsync(
        string consumerName,
        Guid messageId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken = default);
}
