using System.Text;
using System.Text.Json;
using FluxPay.Application.Messaging.Inbox;
using FluxPay.Application.Messaging.Retry;
using FluxPay.Application.Transfers.Events;
using FluxPay.Infrastructure;
using FluxPay.Infrastructure.Messaging;
using FluxPay.Infrastructure.Persistence;
using FluxPay.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace FluxPay.IntegrationTests.Infrastructure;

public sealed class RabbitMqConsumerIntegrationFixture
    : IAsyncLifetime
{
    private const string RabbitMqUsername =
        "fluxpay_integration";

    private const string RabbitMqPassword =
        "fluxpay_integration_password";

    private const string RabbitMqVirtualHost =
        "/";

    private const int RabbitMqPort =
        5672;

    private static readonly JsonSerializerOptions SerializerOptions =
        new(
            JsonSerializerDefaults.Web);

    private readonly PostgreSqlContainer _postgresContainer;

    private readonly RabbitMqContainer _rabbitMqContainer;

    public RabbitMqConsumerIntegrationFixture()
    {
        _postgresContainer =
            new PostgreSqlBuilder(
                "postgres:18.6-alpine3.23")
                .WithDatabase(
                    "fluxpay_consumer_integration")
                .WithUsername(
                    "fluxpay_consumer_integration")
                .WithPassword(
                    "fluxpay_consumer_integration_password")
                .Build();

        _rabbitMqContainer =
            new RabbitMqBuilder(
                "rabbitmq:4.2.9-management-alpine")
                .WithUsername(
                    RabbitMqUsername)
                .WithPassword(
                    RabbitMqPassword)
                .Build();
    }

    public string DatabaseConnectionString =>
        _postgresContainer.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _postgresContainer.StartAsync(),
            _rabbitMqContainer.StartAsync());

        await using var dbContext =
            CreateDbContext();

        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _rabbitMqContainer.DisposeAsync();

        await _postgresContainer.DisposeAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        await DisableInboxInsertFailureAsync();

        await using var dbContext =
            CreateDbContext();

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM consumer_inbox_messages;
            DELETE FROM outbox_messages;
            DELETE FROM transfer_idempotency;
            DELETE FROM transfers;
            DELETE FROM accounts;
            """);
    }

    public RabbitMqOptions CreateRabbitMqOptions(
        string exchangeName)
    {
        return new RabbitMqOptions
        {
            HostName =
                _rabbitMqContainer.Hostname,

            Port =
                _rabbitMqContainer.GetMappedPublicPort(
                    RabbitMqPort),

            UserName =
                RabbitMqUsername,

            Password =
                RabbitMqPassword,

            VirtualHost =
                RabbitMqVirtualHost,

            ExchangeName =
                exchangeName,

            ClientProvidedName =
                "fluxpay-integration-test-publisher"
        };
    }

    public static TransferCompletedConsumerOptions CreateConsumerOptions(
        string suffix)
    {
        return new TransferCompletedConsumerOptions
        {
            ConsumerName =
                $"transfer-completed-consumer-it-{suffix}",

            QueueName =
                $"fluxpay.transfer-completed.it.{suffix}",

            RetryExchangeName =
                $"fluxpay.retry.it.{suffix}",

            DeadLetterExchangeName =
                $"fluxpay.dead-letter.it.{suffix}",

            DeadLetterQueueName =
                $"fluxpay.transfer-completed.dlq.it.{suffix}",

            DeadLetterRoutingKey =
                $"transfer.completed.dlq.it.{suffix}",

            ClientProvidedName =
                $"fluxpay-transfer-completed-consumer-it-{suffix}",

            PrefetchCount =
                1
        };
    }

    public ServiceProvider CreateServiceProvider()
    {
        var services =
            new ServiceCollection();

        services.AddLogging(
            logging =>
            {
                logging.SetMinimumLevel(
                    LogLevel.Warning);
            });

        services.AddSingleton(
            TimeProvider.System);

        services.AddInfrastructure(
            DatabaseConnectionString);

        services.AddScoped<
            InboxMessageProcessor>();

        return services.BuildServiceProvider(
            validateScopes:
                true);
    }

    public static TransferCompletedRabbitMqConsumer CreateConsumer(
        IServiceProvider serviceProvider,
        RabbitMqOptions rabbitMqOptions,
        TransferCompletedConsumerOptions consumerOptions)
    {
        var scopeFactory =
            serviceProvider
                .GetRequiredService<IServiceScopeFactory>();

        var logger =
            serviceProvider
                .GetRequiredService<
                    ILogger<TransferCompletedRabbitMqConsumer>>();

        return new TransferCompletedRabbitMqConsumer(
            scopeFactory,
            rabbitMqOptions,
            consumerOptions,
            logger);
    }

    public async Task WaitForConsumerAsync(
        string queueName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline =
            DateTimeOffset.UtcNow
            + timeout;

        Exception? lastException =
            null;

        while (
            DateTimeOffset.UtcNow
            < deadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            try
            {
                var queueState =
                    await GetQueueStateAsync(
                        queueName,
                        cancellationToken);

                if (
                    queueState.ConsumerCount
                    >= 1)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                lastException =
                    exception;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(
                    100),
                cancellationToken);
        }

        throw new TimeoutException(
            $"RabbitMQ consumer for queue '{queueName}' did not become ready within {timeout}.",
            lastException);
    }

    public Task PublishTransferCompletedAsync(
        RabbitMqOptions rabbitMqOptions,
        TransferCompletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        return PublishTransferCompletedBatchAsync(
            rabbitMqOptions,
            [
                integrationEvent
            ],
            cancellationToken);
    }

    public async Task PublishTransferCompletedBatchAsync(
        RabbitMqOptions rabbitMqOptions,
        IReadOnlyCollection<TransferCompletedIntegrationEvent> integrationEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            integrationEvents);

        await using var connection =
            await CreateRabbitMqConnectionAsync(
                cancellationToken);

        var channelOptions =
            new CreateChannelOptions(
                publisherConfirmationsEnabled:
                    true,
                publisherConfirmationTrackingEnabled:
                    true);

        await using var channel =
            await connection.CreateChannelAsync(
                channelOptions,
                cancellationToken);

        foreach (
            var integrationEvent
            in integrationEvents)
        {
            var payload =
                JsonSerializer.Serialize(
                    integrationEvent,
                    SerializerOptions);

            var body =
                Encoding.UTF8.GetBytes(
                    payload);

            var properties =
                CreateProperties(
                    integrationEvent.EventId,
                    integrationEvent.TransferId);

            await channel.BasicPublishAsync(
                exchange:
                    rabbitMqOptions.ExchangeName,
                routingKey:
                    TransferCompletedIntegrationEvent.EventType,
                mandatory:
                    true,
                basicProperties:
                    properties,
                body:
                    body,
                cancellationToken:
                    cancellationToken);
        }
    }

    public async Task PublishInvalidJsonAsync(
        RabbitMqOptions rabbitMqOptions,
        Guid messageId,
        Guid transferId,
        CancellationToken cancellationToken = default)
    {
        var body =
            Encoding.UTF8.GetBytes(
                """{"eventId":""");

        var properties =
            CreateProperties(
                messageId,
                transferId);

        await using var connection =
            await CreateRabbitMqConnectionAsync(
                cancellationToken);

        var channelOptions =
            new CreateChannelOptions(
                publisherConfirmationsEnabled:
                    true,
                publisherConfirmationTrackingEnabled:
                    true);

        await using var channel =
            await connection.CreateChannelAsync(
                channelOptions,
                cancellationToken);

        await channel.BasicPublishAsync(
            exchange:
                rabbitMqOptions.ExchangeName,
            routingKey:
                TransferCompletedIntegrationEvent.EventType,
            mandatory:
                true,
            basicProperties:
                properties,
            body:
                body,
            cancellationToken:
                cancellationToken);
    }

    public async Task WaitForProcessedInboxAsync(
        string consumerName,
        Guid messageId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline =
            DateTimeOffset.UtcNow
            + timeout;

        while (
            DateTimeOffset.UtcNow
            < deadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var processedCount =
                await GetProcessedInboxCountAsync(
                    consumerName,
                    messageId,
                    cancellationToken);

            if (
                processedCount
                == 1)
            {
                return;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(
                    100),
                cancellationToken);
        }

        throw new TimeoutException(
            $"Inbox message '{messageId:D}' for consumer '{consumerName}' was not processed within {timeout}.");
    }

    public async Task<int> GetProcessedInboxCountAsync(
        string consumerName,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            CreateDbContext();

        return await dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT COUNT(*)::int AS "Value"
                FROM consumer_inbox_messages
                WHERE consumer_name = {consumerName}
                  AND message_id = {messageId}
                  AND processed_at IS NOT NULL
                """)
            .SingleAsync(
                cancellationToken);
    }

    public async Task<int> GetInboxCountAsync(
        string consumerName,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            CreateDbContext();

        return await dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT COUNT(*)::int AS "Value"
                FROM consumer_inbox_messages
                WHERE consumer_name = {consumerName}
                  AND message_id = {messageId}
                """)
            .SingleAsync(
                cancellationToken);
    }

    public async Task WaitForQueueMessageCountAsync(
        string queueName,
        uint expectedMessageCount,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline =
            DateTimeOffset.UtcNow
            + timeout;

        Exception? lastException =
            null;

        while (
            DateTimeOffset.UtcNow
            < deadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            try
            {
                var queueState =
                    await GetQueueStateAsync(
                        queueName,
                        cancellationToken);

                if (
                    queueState.MessageCount
                    == expectedMessageCount)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                lastException =
                    exception;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(
                    100),
                cancellationToken);
        }

        throw new TimeoutException(
            $"RabbitMQ queue '{queueName}' did not reach message count '{expectedMessageCount}' within {timeout}.",
            lastException);
    }

    public async Task<RabbitMqQueueState> GetQueueStateAsync(
        string queueName,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await CreateRabbitMqConnectionAsync(
                cancellationToken);

        await using var channel =
            await connection.CreateChannelAsync(
                cancellationToken:
                    cancellationToken);

        var result =
            await channel.QueueDeclarePassiveAsync(
                queue:
                    queueName,
                cancellationToken:
                    cancellationToken);

        return new RabbitMqQueueState(
            result.MessageCount,
            result.ConsumerCount);
    }

    public static string GetRetryQueueName(
        TransferCompletedConsumerOptions consumerOptions,
        int failedAttemptCount)
    {
        var delay =
            ConsumerRetryPolicy.GetDelay(
                failedAttemptCount);

        var totalSeconds =
            checked(
                (int)
                    delay.TotalSeconds);

        return
            $"{consumerOptions.QueueName}.retry.{totalSeconds}s";
    }

    public async Task EnableInboxInsertFailureAsync()
    {
        await using var dbContext =
            CreateDbContext();

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS tr_fluxpay_it_fail_inbox_insert
            ON consumer_inbox_messages;

            DROP FUNCTION IF EXISTS fluxpay_it_fail_inbox_insert();

            CREATE FUNCTION fluxpay_it_fail_inbox_insert()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                RAISE EXCEPTION 'FluxPay integration test transient inbox failure';
            END;
            $$;

            CREATE TRIGGER tr_fluxpay_it_fail_inbox_insert
            BEFORE INSERT
            ON consumer_inbox_messages
            FOR EACH ROW
            EXECUTE FUNCTION fluxpay_it_fail_inbox_insert();
            """);
    }

    public async Task DisableInboxInsertFailureAsync()
    {
        await using var dbContext =
            CreateDbContext();

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS tr_fluxpay_it_fail_inbox_insert
            ON consumer_inbox_messages;

            DROP FUNCTION IF EXISTS fluxpay_it_fail_inbox_insert();
            """);
    }

    private static BasicProperties CreateProperties(
        Guid messageId,
        Guid transferId)
    {
        return new BasicProperties
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
                transferId.ToString(
                    "D"),

            Type =
                TransferCompletedIntegrationEvent.EventType,

            AppId =
                "fluxpay"
        };
    }

    private FluxPayDbContext CreateDbContext()
    {
        var options =
            new DbContextOptionsBuilder<FluxPayDbContext>()
                .UseNpgsql(
                    DatabaseConnectionString)
                .Options;

        return new FluxPayDbContext(
            options);
    }

    private async Task<IConnection> CreateRabbitMqConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connectionFactory =
            new ConnectionFactory
            {
                HostName =
                    _rabbitMqContainer.Hostname,

                Port =
                    _rabbitMqContainer.GetMappedPublicPort(
                        RabbitMqPort),

                UserName =
                    RabbitMqUsername,

                Password =
                    RabbitMqPassword,

                VirtualHost =
                    RabbitMqVirtualHost,

                ClientProvidedName =
                    "fluxpay-consumer-integration-test"
            };

        return await connectionFactory
            .CreateConnectionAsync(
                cancellationToken);
    }
}

public sealed record RabbitMqQueueState(
    uint MessageCount,
    uint ConsumerCount);
