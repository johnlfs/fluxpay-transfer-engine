using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Common.Time;

namespace FluxPay.Application.Messaging.Inbox;

public sealed class InboxMessageProcessor
{
    private readonly IInboxRepository _inboxRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly TimeProvider _timeProvider;

    public InboxMessageProcessor(
        IInboxRepository inboxRepository,
        ITransactionManager transactionManager,
        TimeProvider timeProvider)
    {
        _inboxRepository =
            inboxRepository;

        _transactionManager =
            transactionManager;

        _timeProvider =
            timeProvider;
    }

    public async Task<InboxProcessingStatus> ProcessAsync(
        string consumerName,
        Guid messageId,
        string eventType,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(consumerName))
        {
            throw new ArgumentException(
                "Consumer name is required.",
                nameof(consumerName));
        }

        if (messageId == Guid.Empty)
        {
            throw new ArgumentException(
                "Message id cannot be empty.",
                nameof(messageId));
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException(
                "Event type is required.",
                nameof(eventType));
        }

        ArgumentNullException.ThrowIfNull(
            handler);

        var receivedAt =
            GetUtcNow();

        return await _transactionManager.ExecuteAsync(
            async transactionCancellationToken =>
            {
                var claimed =
                    await _inboxRepository.TryClaimAsync(
                        consumerName,
                        messageId,
                        eventType,
                        receivedAt,
                        transactionCancellationToken);

                if (!claimed)
                {
                    return InboxProcessingStatus.Duplicate;
                }

                await handler(
                    transactionCancellationToken);

                await _inboxRepository.MarkProcessedAsync(
                    consumerName,
                    messageId,
                    GetUtcNow(),
                    transactionCancellationToken);

                return InboxProcessingStatus.Processed;
            },
            cancellationToken);
    }

    private DateTimeOffset GetUtcNow()
    {
        return UtcTimestamp.GetUtcNow(
            _timeProvider);
    }
}
