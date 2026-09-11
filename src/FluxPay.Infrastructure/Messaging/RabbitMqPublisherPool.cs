using FluxPay.Application.Abstractions.Messaging;

namespace FluxPay.Infrastructure.Messaging;

public sealed class RabbitMqPublisherPool
    : IIntegrationEventPublisher,
      IAsyncDisposable
{
    private readonly RabbitMqPublisher[] _publishers;

    private int _nextPublisher =
        -1;

    private int _disposed;

    public RabbitMqPublisherPool(
        RabbitMqOptions options,
        int publisherCount)
    {
        ArgumentNullException.ThrowIfNull(
            options);

        options.Validate();

        if (
            publisherCount <= 0
            || publisherCount > 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(publisherCount),
                publisherCount,
                "Publisher count must be between 1 and 16.");
        }

        _publishers =
            Enumerable
                .Range(
                    1,
                    publisherCount)
                .Select(
                    index =>
                        CreatePublisher(
                            options,
                            index))
                .ToArray();
    }

    public Task PublishAsync(
        Guid messageId,
        string eventType,
        Guid aggregateId,
        string payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var next =
            Interlocked.Increment(
                ref _nextPublisher);

        var index =
            (int)(
                (uint)next
                % (uint)_publishers.Length
            );

        return _publishers[
                index
            ]
            .PublishAsync(
                messageId,
                eventType,
                aggregateId,
                payload,
                cancellationToken);
    }

    private static RabbitMqPublisher CreatePublisher(
        RabbitMqOptions options,
        int index)
    {
        var clientName =
            string.IsNullOrWhiteSpace(
                options.ClientProvidedName)
                ? "fluxpay"
                : options.ClientProvidedName;

        var publisherOptions =
            new RabbitMqOptions
            {
                HostName =
                    options.HostName,

                Port =
                    options.Port,

                UserName =
                    options.UserName,

                Password =
                    options.Password,

                VirtualHost =
                    options.VirtualHost,

                ExchangeName =
                    options.ExchangeName,

                ClientProvidedName =
                    $"{clientName}-publisher-{index}"
            };

        return new RabbitMqPublisher(
            publisherOptions);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(
                ref _disposed)
            != 0,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (
            Interlocked.Exchange(
                ref _disposed,
                1)
            != 0)
        {
            return;
        }

        Exception? firstException =
            null;

        foreach (
            var publisher
            in _publishers)
        {
            try
            {
                await publisher.DisposeAsync();
            }
            catch (Exception exception)
            {
                firstException ??=
                    exception;
            }
        }

        GC.SuppressFinalize(
            this);

        if (firstException is not null)
        {
            throw firstException;
        }
    }
}
