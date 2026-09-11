using System.Text.Json;
using FluxPay.Application.Messaging.Inbox;
using FluxPay.Application.Transfers.Events;
using FluxPay.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FluxPay.Worker;

public sealed class TransferCompletedRabbitMqConsumer
    : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(
            JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly TransferCompletedConsumerOptions _consumerOptions;
    private readonly ILogger<TransferCompletedRabbitMqConsumer> _logger;

    private IConnection? _connection;

    private IChannel? _channel;

    private AsyncEventingBasicConsumer? _consumer;

    private string? _consumerTag;

    public TransferCompletedRabbitMqConsumer(
        IServiceScopeFactory scopeFactory,
        RabbitMqOptions rabbitMqOptions,
        TransferCompletedConsumerOptions consumerOptions,
        ILogger<TransferCompletedRabbitMqConsumer> logger)
    {
        _scopeFactory =
            scopeFactory;

        _rabbitMqOptions =
            rabbitMqOptions;

        _consumerOptions =
            consumerOptions;

        _logger =
            logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "FluxPay transfer-completed consumer starting. Queue={QueueName}, ConsumerName={ConsumerName}, PrefetchCount={PrefetchCount}.",
            _consumerOptions.QueueName,
            _consumerOptions.ConsumerName,
            _consumerOptions.PrefetchCount);

        try
        {
            await InitializeAsync(
                stoppingToken);

            _logger.LogInformation(
                "FluxPay transfer-completed consumer started. Queue={QueueName}.",
                _consumerOptions.QueueName);

            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
            }
        }
        finally
        {
            await ShutdownAsync();

            _logger.LogInformation(
                "FluxPay transfer-completed consumer stopped.");
        }
    }

    private async Task InitializeAsync(
        CancellationToken cancellationToken)
    {
        _rabbitMqOptions.Validate();
        _consumerOptions.Validate();

        var connectionFactory =
            new ConnectionFactory
            {
                HostName =
                    _rabbitMqOptions.HostName,

                Port =
                    _rabbitMqOptions.Port,

                UserName =
                    _rabbitMqOptions.UserName,

                Password =
                    _rabbitMqOptions.Password,

                VirtualHost =
                    _rabbitMqOptions.VirtualHost,

                ClientProvidedName =
                    _consumerOptions.ClientProvidedName,

                AutomaticRecoveryEnabled =
                    true,

                TopologyRecoveryEnabled =
                    true,

                RequestedHeartbeat =
                    TimeSpan.FromSeconds(
                        30)
            };

        _connection =
            await connectionFactory
                .CreateConnectionAsync(
                    cancellationToken);

        _channel =
            await _connection.CreateChannelAsync(
                cancellationToken:
                    cancellationToken);

        await DeclareTopologyAsync(
            cancellationToken);

        await _channel.BasicQosAsync(
            prefetchSize:
                0,
            prefetchCount:
                checked(
                    (ushort)
                        _consumerOptions.PrefetchCount),
            global:
                false,
            cancellationToken:
                cancellationToken);

        _consumer =
            new AsyncEventingBasicConsumer(
                _channel);

        _consumer.ReceivedAsync +=
            HandleDeliveryAsync;

        _consumerTag =
            await _channel.BasicConsumeAsync(
                queue:
                    _consumerOptions.QueueName,
                autoAck:
                    false,
                consumerTag:
                    _consumerOptions.ConsumerName,
                noLocal:
                    false,
                exclusive:
                    false,
                arguments:
                    null,
                consumer:
                    _consumer,
                cancellationToken:
                    cancellationToken);
    }

    private async Task DeclareTopologyAsync(
        CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException(
                "RabbitMQ channel has not been created.");
        }

        await _channel.ExchangeDeclareAsync(
            exchange:
                _rabbitMqOptions.ExchangeName,
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

        await _channel.ExchangeDeclareAsync(
            exchange:
                _consumerOptions.DeadLetterExchangeName,
            type:
                ExchangeType.Direct,
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

        await _channel.QueueDeclareAsync(
            queue:
                _consumerOptions.DeadLetterQueueName,
            durable:
                true,
            exclusive:
                false,
            autoDelete:
                false,
            arguments:
                null,
            noWait:
                false,
            cancellationToken:
                cancellationToken);

        await _channel.QueueBindAsync(
            queue:
                _consumerOptions.DeadLetterQueueName,
            exchange:
                _consumerOptions.DeadLetterExchangeName,
            routingKey:
                _consumerOptions.DeadLetterRoutingKey,
            arguments:
                null,
            noWait:
                false,
            cancellationToken:
                cancellationToken);

        var mainQueueArguments =
            new Dictionary<string, object?>
            {
                [
                    "x-dead-letter-exchange"
                ] =
                    _consumerOptions
                        .DeadLetterExchangeName,

                [
                    "x-dead-letter-routing-key"
                ] =
                    _consumerOptions
                        .DeadLetterRoutingKey
            };

        await _channel.QueueDeclareAsync(
            queue:
                _consumerOptions.QueueName,
            durable:
                true,
            exclusive:
                false,
            autoDelete:
                false,
            arguments:
                mainQueueArguments,
            noWait:
                false,
            cancellationToken:
                cancellationToken);

        await _channel.QueueBindAsync(
            queue:
                _consumerOptions.QueueName,
            exchange:
                _rabbitMqOptions.ExchangeName,
            routingKey:
                TransferCompletedIntegrationEvent.EventType,
            arguments:
                null,
            noWait:
                false,
            cancellationToken:
                cancellationToken);
    }

    private async Task HandleDeliveryAsync(
        object sender,
        BasicDeliverEventArgs eventArgs)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            var envelope =
                ValidateAndDeserialize(
                    eventArgs);

            await using var scope =
                _scopeFactory.CreateAsyncScope();

            var processor =
                scope.ServiceProvider
                    .GetRequiredService<InboxMessageProcessor>();

            var result =
                await processor.ProcessAsync(
                    _consumerOptions.ConsumerName,
                    envelope.MessageId,
                    TransferCompletedIntegrationEvent.EventType,
                    cancellationToken =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        _logger.LogInformation(
                            "Processing transfer-completed event. MessageId={MessageId}, TransferId={TransferId}, SourceAccountId={SourceAccountId}, DestinationAccountId={DestinationAccountId}, Amount={Amount}.",
                            envelope.MessageId,
                            envelope.Event.TransferId,
                            envelope.Event.SourceAccountId,
                            envelope.Event.DestinationAccountId,
                            envelope.Event.Amount);

                        return Task.CompletedTask;
                    },
                    eventArgs.CancellationToken);

            await _channel.BasicAckAsync(
                deliveryTag:
                    eventArgs.DeliveryTag,
                multiple:
                    false,
                cancellationToken:
                    CancellationToken.None);

            if (
                result
                == InboxProcessingStatus.Duplicate)
            {
                _logger.LogInformation(
                    "Duplicate transfer-completed delivery acknowledged without reprocessing. MessageId={MessageId}.",
                    envelope.MessageId);

                return;
            }

            _logger.LogInformation(
                "Transfer-completed delivery processed and acknowledged. MessageId={MessageId}, TransferId={TransferId}.",
                envelope.MessageId,
                envelope.Event.TransferId);
        }
        catch (OperationCanceledException)
            when (eventArgs.CancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Transfer-completed delivery processing was cancelled. DeliveryTag={DeliveryTag}.",
                eventArgs.DeliveryTag);
        }
        catch (PermanentMessageException exception)
        {
            _logger.LogError(
                exception,
                "Invalid transfer-completed delivery will be dead-lettered. DeliveryTag={DeliveryTag}.",
                eventArgs.DeliveryTag);

            await TryNackAsync(
                eventArgs.DeliveryTag,
                requeue:
                    false);
        }
        catch (Exception exception)
        {
            var shouldRequeue =
                !eventArgs.Redelivered;

            _logger.LogError(
                exception,
                "Transfer-completed delivery failed. DeliveryTag={DeliveryTag}, Redelivered={Redelivered}, Requeue={Requeue}.",
                eventArgs.DeliveryTag,
                eventArgs.Redelivered,
                shouldRequeue);

            await TryNackAsync(
                eventArgs.DeliveryTag,
                requeue:
                    shouldRequeue);
        }
    }

    private DeliveryEnvelope ValidateAndDeserialize(
        BasicDeliverEventArgs eventArgs)
    {
        if (
            !string.Equals(
                eventArgs.Exchange,
                _rabbitMqOptions.ExchangeName,
                StringComparison.Ordinal))
        {
            throw new PermanentMessageException(
                $"Unexpected exchange '{eventArgs.Exchange}'.");
        }

        if (
            !string.Equals(
                eventArgs.RoutingKey,
                TransferCompletedIntegrationEvent.EventType,
                StringComparison.Ordinal))
        {
            throw new PermanentMessageException(
                $"Unexpected routing key '{eventArgs.RoutingKey}'.");
        }

        var properties =
            eventArgs.BasicProperties;

        if (
            !string.Equals(
                properties.Type,
                TransferCompletedIntegrationEvent.EventType,
                StringComparison.Ordinal))
        {
            throw new PermanentMessageException(
                $"Unexpected message type '{properties.Type}'.");
        }

        if (
            !string.Equals(
                properties.ContentType,
                "application/json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PermanentMessageException(
                $"Unexpected content type '{properties.ContentType}'.");
        }

        if (
            !string.Equals(
                properties.AppId,
                "fluxpay",
                StringComparison.Ordinal))
        {
            throw new PermanentMessageException(
                $"Unexpected application id '{properties.AppId}'.");
        }

        if (
            !Guid.TryParse(
                properties.MessageId,
                out var messageId)
            || messageId
                == Guid.Empty)
        {
            throw new PermanentMessageException(
                "RabbitMQ MessageId must contain a valid non-empty UUID.");
        }

        if (
            !Guid.TryParse(
                properties.CorrelationId,
                out var correlationId)
            || correlationId
                == Guid.Empty)
        {
            throw new PermanentMessageException(
                "RabbitMQ CorrelationId must contain a valid non-empty UUID.");
        }

        TransferCompletedIntegrationEvent? integrationEvent;

        try
        {
            integrationEvent =
                JsonSerializer.Deserialize<
                    TransferCompletedIntegrationEvent>(
                        eventArgs.Body.Span,
                        SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new PermanentMessageException(
                "Transfer-completed payload contains invalid JSON.",
                exception);
        }

        if (integrationEvent is null)
        {
            throw new PermanentMessageException(
                "Transfer-completed payload cannot be null.");
        }

        if (
            integrationEvent.EventId
            != messageId)
        {
            throw new PermanentMessageException(
                "Payload EventId does not match RabbitMQ MessageId.");
        }

        if (
            integrationEvent.TransferId
            != correlationId)
        {
            throw new PermanentMessageException(
                "Payload TransferId does not match RabbitMQ CorrelationId.");
        }

        if (
            integrationEvent.TransferId
            == Guid.Empty)
        {
            throw new PermanentMessageException(
                "TransferId cannot be empty.");
        }

        if (
            integrationEvent.SourceAccountId
            == Guid.Empty)
        {
            throw new PermanentMessageException(
                "SourceAccountId cannot be empty.");
        }

        if (
            integrationEvent.DestinationAccountId
            == Guid.Empty)
        {
            throw new PermanentMessageException(
                "DestinationAccountId cannot be empty.");
        }

        if (
            integrationEvent.SourceAccountId
            == integrationEvent.DestinationAccountId)
        {
            throw new PermanentMessageException(
                "Source and destination accounts cannot be the same.");
        }

        if (
            integrationEvent.Amount
            <= 0m)
        {
            throw new PermanentMessageException(
                "Transfer amount must be greater than zero.");
        }

        if (
            integrationEvent.OccurredAt
            == default)
        {
            throw new PermanentMessageException(
                "OccurredAt is required.");
        }

        return new DeliveryEnvelope(
            messageId,
            correlationId,
            integrationEvent);
    }

    private async Task TryNackAsync(
        ulong deliveryTag,
        bool requeue)
    {
        var channel =
            _channel;

        if (
            channel is null
            || !channel.IsOpen)
        {
            return;
        }

        try
        {
            await channel.BasicNackAsync(
                deliveryTag:
                    deliveryTag,
                multiple:
                    false,
                requeue:
                    requeue,
                cancellationToken:
                    CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to NACK RabbitMQ delivery. DeliveryTag={DeliveryTag}, Requeue={Requeue}.",
                deliveryTag,
                requeue);
        }
    }

    private async Task ShutdownAsync()
    {
        var channel =
            _channel;

        _channel =
            null;

        if (
            channel is not null
            && channel.IsOpen
            && !string.IsNullOrWhiteSpace(
                _consumerTag))
        {
            try
            {
                await channel.BasicCancelAsync(
                    _consumerTag,
                    noWait:
                        false,
                    cancellationToken:
                        CancellationToken.None);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "RabbitMQ consumer cancellation failed during shutdown.");
            }
        }

        _consumerTag =
            null;

        _consumer =
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

    private sealed record DeliveryEnvelope(
        Guid MessageId,
        Guid CorrelationId,
        TransferCompletedIntegrationEvent Event);

    private sealed class PermanentMessageException
        : Exception
    {
        public PermanentMessageException(
            string message)
            : base(message)
        {
        }

        public PermanentMessageException(
            string message,
            Exception innerException)
            : base(
                message,
                innerException)
        {
        }
    }
}
