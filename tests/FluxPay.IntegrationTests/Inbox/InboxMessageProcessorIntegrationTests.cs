using FluxPay.Application.Messaging.Inbox;
using FluxPay.Infrastructure.Persistence;
using FluxPay.Infrastructure.Persistence.Inbox;
using FluxPay.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.IntegrationTests.Inbox;

public sealed class InboxMessageProcessorIntegrationTests
    : PostgreSqlIntegrationTestBase
{
    private const string ConsumerName =
        "transfer-completed-consumer";

    private const string EventType =
        "transfer.completed.v1";

    private static readonly DateTimeOffset InitialTime =
        new(
            2026,
            9,
            11,
            7,
            0,
            0,
            TimeSpan.Zero);

    public InboxMessageProcessorIntegrationTests(
        PostgreSqlIntegrationFixture fixture)
        : base(
            fixture)
    {
    }

    [Fact]
    public async Task ProcessAsync_FirstDelivery_ExecutesHandlerAndMarksMessageProcessed()
    {
        var messageId =
            Guid.NewGuid();

        var handlerCalls =
            0;

        var timeProvider =
            new ManualTimeProvider(
                InitialTime);

        await using (
            var dbContext =
                Fixture.CreateDbContext())
        {
            var processor =
                CreateProcessor(
                    dbContext,
                    timeProvider);

            var result =
                await processor.ProcessAsync(
                    ConsumerName,
                    messageId,
                    EventType,
                    cancellationToken =>
                    {
                        handlerCalls++;

                        return Task.CompletedTask;
                    });

            Assert.Equal(
                InboxProcessingStatus.Processed,
                result);
        }

        Assert.Equal(
            1,
            handlerCalls);

        await using var verificationContext =
            Fixture.CreateDbContext();

        var persisted =
            await verificationContext.InboxMessages
                .AsNoTracking()
                .SingleAsync(
                    message =>
                        message.ConsumerName
                            == ConsumerName
                        && message.MessageId
                            == messageId);

        Assert.Equal(
            EventType,
            persisted.EventType);

        Assert.Equal(
            InitialTime,
            persisted.ReceivedAt);

        Assert.Equal(
            InitialTime,
            persisted.ProcessedAt);
    }

    [Fact]
    public async Task ProcessAsync_Redelivery_DoesNotExecuteHandlerTwice()
    {
        var messageId =
            Guid.NewGuid();

        var handlerCalls =
            0;

        var timeProvider =
            new ManualTimeProvider(
                InitialTime);

        await using (
            var firstContext =
                Fixture.CreateDbContext())
        {
            var firstProcessor =
                CreateProcessor(
                    firstContext,
                    timeProvider);

            var firstResult =
                await firstProcessor.ProcessAsync(
                    ConsumerName,
                    messageId,
                    EventType,
                    cancellationToken =>
                    {
                        handlerCalls++;

                        return Task.CompletedTask;
                    });

            Assert.Equal(
                InboxProcessingStatus.Processed,
                firstResult);
        }

        timeProvider.Advance(
            TimeSpan.FromSeconds(
                10));

        await using (
            var duplicateContext =
                Fixture.CreateDbContext())
        {
            var duplicateProcessor =
                CreateProcessor(
                    duplicateContext,
                    timeProvider);

            var duplicateResult =
                await duplicateProcessor.ProcessAsync(
                    ConsumerName,
                    messageId,
                    EventType,
                    cancellationToken =>
                    {
                        handlerCalls++;

                        return Task.CompletedTask;
                    });

            Assert.Equal(
                InboxProcessingStatus.Duplicate,
                duplicateResult);
        }

        Assert.Equal(
            1,
            handlerCalls);

        await using var verificationContext =
            Fixture.CreateDbContext();

        var persisted =
            await verificationContext.InboxMessages
                .AsNoTracking()
                .SingleAsync(
                    message =>
                        message.ConsumerName
                            == ConsumerName
                        && message.MessageId
                            == messageId);

        Assert.Equal(
            InitialTime,
            persisted.ReceivedAt);

        Assert.Equal(
            InitialTime,
            persisted.ProcessedAt);
    }

    [Fact]
    public async Task ProcessAsync_WhenHandlerFails_RollsBackClaimAndAllowsRedelivery()
    {
        var messageId =
            Guid.NewGuid();

        var timeProvider =
            new ManualTimeProvider(
                InitialTime);

        await using (
            var failingContext =
                Fixture.CreateDbContext())
        {
            var failingProcessor =
                CreateProcessor(
                    failingContext,
                    timeProvider);

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () =>
                    {
                        await failingProcessor.ProcessAsync(
                            ConsumerName,
                            messageId,
                            EventType,
                            cancellationToken =>
                            {
                                throw new InvalidOperationException(
                                    "Simulated message handler failure.");
                            });
                    });

            Assert.Equal(
                "Simulated message handler failure.",
                exception.Message);
        }

        await using (
            var rollbackVerificationContext =
                Fixture.CreateDbContext())
        {
            var persistedAfterFailure =
                await rollbackVerificationContext.InboxMessages
                    .AsNoTracking()
                    .CountAsync(
                        message =>
                            message.ConsumerName
                                == ConsumerName
                            && message.MessageId
                                == messageId);

            Assert.Equal(
                0,
                persistedAfterFailure);
        }

        timeProvider.Advance(
            TimeSpan.FromSeconds(
                1));

        var successfulHandlerCalls =
            0;

        await using (
            var retryContext =
                Fixture.CreateDbContext())
        {
            var retryProcessor =
                CreateProcessor(
                    retryContext,
                    timeProvider);

            var retryResult =
                await retryProcessor.ProcessAsync(
                    ConsumerName,
                    messageId,
                    EventType,
                    cancellationToken =>
                    {
                        successfulHandlerCalls++;

                        return Task.CompletedTask;
                    });

            Assert.Equal(
                InboxProcessingStatus.Processed,
                retryResult);
        }

        Assert.Equal(
            1,
            successfulHandlerCalls);

        await using var finalContext =
            Fixture.CreateDbContext();

        var persisted =
            await finalContext.InboxMessages
                .AsNoTracking()
                .SingleAsync(
                    message =>
                        message.ConsumerName
                            == ConsumerName
                        && message.MessageId
                            == messageId);

        Assert.Equal(
            InitialTime.AddSeconds(
                1),
            persisted.ReceivedAt);

        Assert.Equal(
            InitialTime.AddSeconds(
                1),
            persisted.ProcessedAt);
    }

    private static InboxMessageProcessor CreateProcessor(
        FluxPayDbContext dbContext,
        TimeProvider timeProvider)
    {
        var repository =
            new EfInboxRepository(
                dbContext);

        var transactionManager =
            new EfTransactionManager(
                dbContext);

        return new InboxMessageProcessor(
            repository,
            transactionManager,
            timeProvider);
    }

    private sealed class ManualTimeProvider
        : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow =
                utcNow.ToUniversalTime();
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(
            TimeSpan amount)
        {
            if (amount < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount),
                    amount,
                    "Time cannot move backwards.");
            }

            _utcNow =
                _utcNow.Add(
                    amount);
        }
    }
}
