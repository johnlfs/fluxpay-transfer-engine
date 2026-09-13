using FluxPay.Application.Transfers.Events;
using FluxPay.IntegrationTests.Infrastructure;
using FluxPay.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace FluxPay.IntegrationTests.Messaging;

[Collection(
    RabbitMqConsumerIntegrationCollection.Name)]
public sealed class TransferCompletedRabbitMqConsumerIntegrationTests
    : IAsyncLifetime
{
    private readonly RabbitMqConsumerIntegrationFixture _fixture;

    private ServiceProvider? _serviceProvider;

    private TransferCompletedRabbitMqConsumer? _consumer;

    public TransferCompletedRabbitMqConsumerIntegrationTests(
        RabbitMqConsumerIntegrationFixture fixture)
    {
        _fixture =
            fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();

        _serviceProvider =
            _fixture.CreateServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await StopConsumerAsync();

        await _fixture.DisableInboxInsertFailureAsync();

        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();

            _serviceProvider =
                null;
        }
    }

    [Fact]
    public async Task ValidMessage_IsProcessedPersistedAndAcknowledged()
    {
        var context =
            await StartConsumerAsync();

        using var timeout =
            CreateTimeout();

        var integrationEvent =
            CreateIntegrationEvent();

        await _fixture.PublishTransferCompletedAsync(
            context.RabbitMqOptions,
            integrationEvent,
            timeout.Token);

        await _fixture.WaitForProcessedInboxAsync(
            context.ConsumerOptions.ConsumerName,
            integrationEvent.EventId,
            TimeSpan.FromSeconds(
                15),
            timeout.Token);

        var processedCount =
            await _fixture.GetProcessedInboxCountAsync(
                context.ConsumerOptions.ConsumerName,
                integrationEvent.EventId,
                timeout.Token);

        Assert.Equal(
            1,
            processedCount);

        await StopConsumerAsync();

        var queueState =
            await _fixture.GetQueueStateAsync(
                context.ConsumerOptions.QueueName,
                timeout.Token);

        Assert.Equal(
            0u,
            queueState.MessageCount);

        Assert.Equal(
            0u,
            queueState.ConsumerCount);
    }

    [Fact]
    public async Task DuplicateMessage_IsAcknowledgedWithoutSecondInboxEffect()
    {
        var context =
            await StartConsumerAsync();

        using var timeout =
            CreateTimeout();

        var integrationEvent =
            CreateIntegrationEvent();

        await _fixture.PublishTransferCompletedAsync(
            context.RabbitMqOptions,
            integrationEvent,
            timeout.Token);

        await _fixture.WaitForProcessedInboxAsync(
            context.ConsumerOptions.ConsumerName,
            integrationEvent.EventId,
            TimeSpan.FromSeconds(
                15),
            timeout.Token);

        var sentinelEvent =
            CreateIntegrationEvent();

        await _fixture.PublishTransferCompletedBatchAsync(
            context.RabbitMqOptions,
            [
                integrationEvent,
                sentinelEvent
            ],
            timeout.Token);

        await _fixture.WaitForProcessedInboxAsync(
            context.ConsumerOptions.ConsumerName,
            sentinelEvent.EventId,
            TimeSpan.FromSeconds(
                15),
            timeout.Token);

        var duplicateInboxCount =
            await _fixture.GetInboxCountAsync(
                context.ConsumerOptions.ConsumerName,
                integrationEvent.EventId,
                timeout.Token);

        var sentinelInboxCount =
            await _fixture.GetProcessedInboxCountAsync(
                context.ConsumerOptions.ConsumerName,
                sentinelEvent.EventId,
                timeout.Token);

        Assert.Equal(
            1,
            duplicateInboxCount);

        Assert.Equal(
            1,
            sentinelInboxCount);

        await StopConsumerAsync();

        var queueState =
            await _fixture.GetQueueStateAsync(
                context.ConsumerOptions.QueueName,
                timeout.Token);

        Assert.Equal(
            0u,
            queueState.MessageCount);
    }

    [Fact]
    public async Task InvalidMessage_IsDeadLetteredWithoutInboxRecord()
    {
        var context =
            await StartConsumerAsync();

        using var timeout =
            CreateTimeout();

        var messageId =
            Guid.NewGuid();

        var transferId =
            Guid.NewGuid();

        await _fixture.PublishInvalidJsonAsync(
            context.RabbitMqOptions,
            messageId,
            transferId,
            timeout.Token);

        await _fixture.WaitForQueueMessageCountAsync(
            context.ConsumerOptions.DeadLetterQueueName,
            expectedMessageCount:
                1,
            TimeSpan.FromSeconds(
                15),
            timeout.Token);

        var inboxCount =
            await _fixture.GetInboxCountAsync(
                context.ConsumerOptions.ConsumerName,
                messageId,
                timeout.Token);

        Assert.Equal(
            0,
            inboxCount);

        await StopConsumerAsync();

        var mainQueueState =
            await _fixture.GetQueueStateAsync(
                context.ConsumerOptions.QueueName,
                timeout.Token);

        var deadLetterQueueState =
            await _fixture.GetQueueStateAsync(
                context.ConsumerOptions.DeadLetterQueueName,
                timeout.Token);

        Assert.Equal(
            0u,
            mainQueueState.MessageCount);

        Assert.Equal(
            1u,
            deadLetterQueueState.MessageCount);
    }

    [Fact]
    public async Task TransientFailure_IsRetriedAndEventuallyProcessed()
    {
        var context =
            await StartConsumerAsync();

        using var timeout =
            CreateTimeout(
                seconds:
                    45);

        var integrationEvent =
            CreateIntegrationEvent();

        var firstRetryQueueName =
            RabbitMqConsumerIntegrationFixture
                .GetRetryQueueName(
                    context.ConsumerOptions,
                    failedAttemptCount:
                        1);

        await _fixture.EnableInboxInsertFailureAsync();

        try
        {
            await _fixture.PublishTransferCompletedAsync(
                context.RabbitMqOptions,
                integrationEvent,
                timeout.Token);

            await _fixture.WaitForQueueMessageCountAsync(
                firstRetryQueueName,
                expectedMessageCount:
                    1,
                TimeSpan.FromSeconds(
                    15),
                timeout.Token);

            var inboxCountBeforeRetry =
                await _fixture.GetInboxCountAsync(
                    context.ConsumerOptions.ConsumerName,
                    integrationEvent.EventId,
                    timeout.Token);

            Assert.Equal(
                0,
                inboxCountBeforeRetry);

            await _fixture.DisableInboxInsertFailureAsync();

            await _fixture.WaitForProcessedInboxAsync(
                context.ConsumerOptions.ConsumerName,
                integrationEvent.EventId,
                TimeSpan.FromSeconds(
                    20),
                timeout.Token);

            var processedCount =
                await _fixture.GetProcessedInboxCountAsync(
                    context.ConsumerOptions.ConsumerName,
                    integrationEvent.EventId,
                    timeout.Token);

            Assert.Equal(
                1,
                processedCount);
        }
        finally
        {
            await _fixture.DisableInboxInsertFailureAsync();
        }

        await StopConsumerAsync();

        var mainQueueState =
            await _fixture.GetQueueStateAsync(
                context.ConsumerOptions.QueueName,
                timeout.Token);

        var retryQueueState =
            await _fixture.GetQueueStateAsync(
                firstRetryQueueName,
                timeout.Token);

        var deadLetterQueueState =
            await _fixture.GetQueueStateAsync(
                context.ConsumerOptions.DeadLetterQueueName,
                timeout.Token);

        Assert.Equal(
            0u,
            mainQueueState.MessageCount);

        Assert.Equal(
            0u,
            retryQueueState.MessageCount);

        Assert.Equal(
            0u,
            deadLetterQueueState.MessageCount);
    }

    private async Task<ConsumerTestContext> StartConsumerAsync()
    {
        var suffix =
            Guid.NewGuid()
                .ToString(
                    "N");

        var rabbitMqOptions =
            _fixture.CreateRabbitMqOptions(
                $"fluxpay.events.it.{suffix}");

        var consumerOptions =
            RabbitMqConsumerIntegrationFixture
                .CreateConsumerOptions(
                    suffix);

        _consumer =
            RabbitMqConsumerIntegrationFixture
                .CreateConsumer(
                    _serviceProvider
                    ?? throw new InvalidOperationException(
                        "The test service provider has not been initialized."),
                    rabbitMqOptions,
                    consumerOptions);

        await _consumer.StartAsync(
            CancellationToken.None);

        using var timeout =
            CreateTimeout();

        await _fixture.WaitForConsumerAsync(
            consumerOptions.QueueName,
            TimeSpan.FromSeconds(
                15),
            timeout.Token);

        return new ConsumerTestContext(
            rabbitMqOptions,
            consumerOptions);
    }

    private static TransferCompletedIntegrationEvent CreateIntegrationEvent()
    {
        return new TransferCompletedIntegrationEvent(
            EventId:
                Guid.NewGuid(),
            TransferId:
                Guid.NewGuid(),
            SourceAccountId:
                Guid.NewGuid(),
            DestinationAccountId:
                Guid.NewGuid(),
            Amount:
                125.50m,
            OccurredAt:
                DateTimeOffset.UtcNow);
    }

    private static CancellationTokenSource CreateTimeout(
        int seconds = 30)
    {
        return new CancellationTokenSource(
            TimeSpan.FromSeconds(
                seconds));
    }

    private async Task StopConsumerAsync()
    {
        var consumer =
            _consumer;

        _consumer =
            null;

        if (consumer is null)
        {
            return;
        }

        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(
                    10));

        await consumer.StopAsync(
            timeout.Token);

        consumer.Dispose();
    }

    private sealed record ConsumerTestContext(
        FluxPay.Infrastructure.Messaging.RabbitMqOptions RabbitMqOptions,
        TransferCompletedConsumerOptions ConsumerOptions);
}
