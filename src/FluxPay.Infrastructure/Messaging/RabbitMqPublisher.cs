using System.Text;
using FluxPay.Application.Abstractions.Messaging;
using FluxPay.Infrastructure.Observability;
using RabbitMQ.Client;

namespace FluxPay.Infrastructure.Messaging;

public sealed class RabbitMqPublisher
    : IIntegrationEventPublisher,
      IAsyncDisposable
{
    private readonly RabbitMqOptions _options;

    private readonly SemaphoreSlim _operationLock =
        new(
            1,
            1);

    private IConnection? _connection;

    private IChannel? _channel;

    private bool _disposed;

    public RabbitMqPublisher(
        RabbitMqOptions options)
    {
        options.Validate();

        _options =
            options;
    }

    public async Task PublishAsync(
        Guid messageId,
        string eventType,
        Guid aggregateId,
        string payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

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

        if (aggregateId == Guid.Empty)
        {
            throw new ArgumentException(
                "Aggregate id cannot be empty.",
                nameof(aggregateId));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException(
                "Event payload is required.",
                nameof(payload));
        }

        await _operationLock.WaitAsync(
            cancellationToken);

        try
        {
            ThrowIfDisposed();

            await EnsureConnectionAsync(
                cancellationToken);

            var headers =
                new Dictionary<string, object?>();

            TraceContextHeaders.Inject(
                headers);

            var properties =
                new BasicProperties
                {
                    ContentType =
                        "application/json",

                    ContentEncoding =
                        "utf-8",

                    Persistent =
                        true,

                    MessageId =
                        messageId.ToString(
                            "D"),

                    CorrelationId =
                        aggregateId.ToString(
                            "D"),

                    Type =
                        eventType,

                    AppId =
                        "fluxpay",

                    Headers =
                        headers.Count == 0
                            ? null
                            : headers
                };

            var body =
                Encoding.UTF8.GetBytes(
                    payload);

            await _channel!.BasicPublishAsync(
                exchange:
                    _options.ExchangeName,
                routingKey:
                    eventType,
                mandatory:
                    true,
                basicProperties:
                    properties,
                body:
                    body,
                cancellationToken:
                    cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task EnsureConnectionAsync(
        CancellationToken cancellationToken)
    {
        if (
            _connection is
            {
                IsOpen: true
            }
            && _channel is
            {
                IsOpen: true
            })
        {
            return;
        }

        await DisposeTransportAsync();

        var connectionFactory =
            new ConnectionFactory
            {
                HostName =
                    _options.HostName,

                Port =
                    _options.Port,

                UserName =
                    _options.UserName,

                Password =
                    _options.Password,

                VirtualHost =
                    _options.VirtualHost,

                ClientProvidedName =
                    _options.ClientProvidedName,

                AutomaticRecoveryEnabled =
                    true,

                TopologyRecoveryEnabled =
                    true,

                RequestedHeartbeat =
                    TimeSpan.FromSeconds(
                        30)
            };

        try
        {
            _connection =
                await connectionFactory
                    .CreateConnectionAsync(
                        cancellationToken);

            var channelOptions =
                new CreateChannelOptions(
                    publisherConfirmationsEnabled:
                        true,
                    publisherConfirmationTrackingEnabled:
                        true);

            _channel =
                await _connection
                    .CreateChannelAsync(
                        channelOptions,
                        cancellationToken);

            await _channel.ExchangeDeclareAsync(
                exchange:
                    _options.ExchangeName,
                type:
                    ExchangeType.Topic,
                durable:
                    true,
                autoDelete:
                    false,
                arguments:
                    null,
                noWait:
                    false,
                cancellationToken:
                    cancellationToken);
        }
        catch
        {
            await DisposeTransportAsync();

            throw;
        }
    }

    private async ValueTask DisposeTransportAsync()
    {
        var channel =
            _channel;

        _channel =
            null;

        if (channel is not null)
        {
            await channel.DisposeAsync();
        }

        var connection =
            _connection;

        _connection =
            null;

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _operationLock.WaitAsync();

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed =
                true;

            await DisposeTransportAsync();
        }
        finally
        {
            _operationLock.Release();
        }

        _operationLock.Dispose();

        GC.SuppressFinalize(
            this);
    }
}
