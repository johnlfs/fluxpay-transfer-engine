using FluxPay.Application.Abstractions.Messaging;
using FluxPay.Infrastructure.Persistence.Outbox;
using FluxPay.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.IntegrationTests.Outbox;

public sealed class OutboxProcessorIntegrationTests
    : PostgreSqlIntegrationTestBase
{
    private static readonly DateTimeOffset InitialTime =
        new(
            2026,
            9,
            11,
            5,
            30,
            0,
            TimeSpan.Zero);

    public OutboxProcessorIntegrationTests(
        PostgreSqlIntegrationFixture fixture)
        : base(
            fixture)
    {
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenPublishSucceeds_MarksMessageAsPublished()
    {
        var timeProvider =
            new ManualTimeProvider(
                InitialTime);

        var messageId =
            await SeedOutboxMessageAsync(
                InitialTime);

        var publisher =
            new FakeIntegrationEventPublisher();

        await using var dbContext =
            Fixture.CreateDbContext();

        var processor =
            new OutboxProcessor(
                dbContext,
                publisher,
                timeProvider);

        var result =
            await processor.ProcessBatchAsync(
                batchSize: 10);

        Assert.Equal(
            1,
            result.Candidates);

        Assert.Equal(
            1,
            result.Published);

        Assert.Equal(
            0,
            result.Failed);

        Assert.Equal(
            0,
            result.DeadLettered);

        Assert.Equal(
            0,
            result.Skipped);

        Assert.Equal(
            1,
            publisher.PublishCallCount);

        await using var verificationContext =
            Fixture.CreateDbContext();

        var persisted =
            await verificationContext.OutboxMessages
                .AsNoTracking()
                .SingleAsync(
                    message =>
                        message.Id == messageId);

        Assert.Equal(
            InitialTime,
            persisted.PublishedAt);

        Assert.Equal(
            1,
            persisted.AttemptCount);

        Assert.Null(
            persisted.NextAttemptAt);

        Assert.Null(
            persisted.DeadLetteredAt);

        Assert.Null(
            persisted.LastError);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenFirstPublishFails_SchedulesRetryAfterFiveSeconds()
    {
        var timeProvider =
            new ManualTimeProvider(
                InitialTime);

        var messageId =
            await SeedOutboxMessageAsync(
                InitialTime);

        var publisher =
            new FakeIntegrationEventPublisher(
                failure:
                    new InvalidOperationException(
                        "Broker unavailable."));

        await using var dbContext =
            Fixture.CreateDbContext();

        var processor =
            new OutboxProcessor(
                dbContext,
                publisher,
                timeProvider);

        var result =
            await processor.ProcessBatchAsync(
                batchSize: 10);

        Assert.Equal(
            1,
            result.Candidates);

        Assert.Equal(
            0,
            result.Published);

        Assert.Equal(
            1,
            result.Failed);

        Assert.Equal(
            0,
            result.DeadLettered);

        Assert.Equal(
            1,
            publisher.PublishCallCount);

        await using var verificationContext =
            Fixture.CreateDbContext();

        var persisted =
            await verificationContext.OutboxMessages
                .AsNoTracking()
                .SingleAsync(
                    message =>
                        message.Id == messageId);

        Assert.Null(
            persisted.PublishedAt);

        Assert.Equal(
            1,
            persisted.AttemptCount);

        Assert.Equal(
            InitialTime.AddSeconds(
                5),
            persisted.NextAttemptAt);

        Assert.Null(
            persisted.DeadLetteredAt);

        Assert.NotNull(
            persisted.LastError);

        Assert.Contains(
            "Broker unavailable.",
            persisted.LastError);
    }

    [Fact]
    public async Task ProcessBatchAsync_BeforeNextAttemptAt_DoesNotRetryMessage()
    {
        var timeProvider =
            new ManualTimeProvider(
                InitialTime);

        await SeedOutboxMessageAsync(
            InitialTime);

        var publisher =
            new FakeIntegrationEventPublisher(
                failure:
                    new InvalidOperationException(
                        "Broker unavailable."));

        await using var firstContext =
            Fixture.CreateDbContext();

        var firstProcessor =
            new OutboxProcessor(
                firstContext,
                publisher,
                timeProvider);

        var firstResult =
            await firstProcessor.ProcessBatchAsync(
                batchSize: 10);

        Assert.Equal(
            1,
            firstResult.Failed);

        Assert.Equal(
            1,
            publisher.PublishCallCount);

        timeProvider.Advance(
            TimeSpan.FromSeconds(
                4));

        await using var secondContext =
            Fixture.CreateDbContext();

        var secondProcessor =
            new OutboxProcessor(
                secondContext,
                publisher,
                timeProvider);

        var secondResult =
            await secondProcessor.ProcessBatchAsync(
                batchSize: 10);

        Assert.Equal(
            0,
            secondResult.Candidates);

        Assert.Equal(
            0,
            secondResult.Published);

        Assert.Equal(
            0,
            secondResult.Failed);

        Assert.Equal(
            0,
            secondResult.DeadLettered);

        Assert.Equal(
            0,
            secondResult.Skipped);

        Assert.Equal(
            1,
            publisher.PublishCallCount);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenRetryBecomesDue_PublishesSuccessfully()
    {
        var timeProvider =
            new ManualTimeProvider(
                InitialTime);

        var messageId =
            await SeedOutboxMessageAsync(
                InitialTime);

        await using (
            var firstContext =
                Fixture.CreateDbContext())
        {
            var failingPublisher =
                new FakeIntegrationEventPublisher(
                    failure:
                        new InvalidOperationException(
                            "First attempt failed."));

            var firstProcessor =
                new OutboxProcessor(
                    firstContext,
                    failingPublisher,
                    timeProvider);

            var firstResult =
                await firstProcessor.ProcessBatchAsync(
                    batchSize: 10);

            Assert.Equal(
                1,
                firstResult.Failed);
        }

        timeProvider.Advance(
            TimeSpan.FromSeconds(
                5));

        await using (
            var retryContext =
                Fixture.CreateDbContext())
        {
            var successfulPublisher =
                new FakeIntegrationEventPublisher();

            var retryProcessor =
                new OutboxProcessor(
                    retryContext,
                    successfulPublisher,
                    timeProvider);

            var retryResult =
                await retryProcessor.ProcessBatchAsync(
                    batchSize: 10);

            Assert.Equal(
                1,
                retryResult.Published);

            Assert.Equal(
                0,
                retryResult.Failed);

            Assert.Equal(
                0,
                retryResult.DeadLettered);

            Assert.Equal(
                1,
                successfulPublisher.PublishCallCount);
        }

        await using var verificationContext =
            Fixture.CreateDbContext();

        var persisted =
            await verificationContext.OutboxMessages
                .AsNoTracking()
                .SingleAsync(
                    message =>
                        message.Id == messageId);

        Assert.Equal(
            InitialTime.AddSeconds(
                5),
            persisted.PublishedAt);

        Assert.Equal(
            2,
            persisted.AttemptCount);

        Assert.Null(
            persisted.NextAttemptAt);

        Assert.Null(
            persisted.DeadLetteredAt);

        Assert.Null(
            persisted.LastError);
    }

    [Fact]
    public async Task ProcessBatchAsync_AfterFifthFailure_DeadLettersMessage()
    {
        var timeProvider =
            new ManualTimeProvider(
                InitialTime);

        var messageId =
            await SeedOutboxMessageAsync(
                InitialTime);

        var publisher =
            new FakeIntegrationEventPublisher(
                failure:
                    new InvalidOperationException(
                        "Permanent broker failure."));

        await ProcessFailureAsync(
            publisher,
            timeProvider);

        timeProvider.Advance(
            TimeSpan.FromSeconds(
                5));

        await ProcessFailureAsync(
            publisher,
            timeProvider);

        timeProvider.Advance(
            TimeSpan.FromSeconds(
                15));

        await ProcessFailureAsync(
            publisher,
            timeProvider);

        timeProvider.Advance(
            TimeSpan.FromSeconds(
                45));

        await ProcessFailureAsync(
            publisher,
            timeProvider);

        timeProvider.Advance(
            TimeSpan.FromSeconds(
                135));

        await using (
            var fifthContext =
                Fixture.CreateDbContext())
        {
            var fifthProcessor =
                new OutboxProcessor(
                    fifthContext,
                    publisher,
                    timeProvider);

            var fifthResult =
                await fifthProcessor.ProcessBatchAsync(
                    batchSize: 10);

            Assert.Equal(
                1,
                fifthResult.Candidates);

            Assert.Equal(
                0,
                fifthResult.Published);

            Assert.Equal(
                0,
                fifthResult.Failed);

            Assert.Equal(
                1,
                fifthResult.DeadLettered);

            Assert.Equal(
                0,
                fifthResult.Skipped);
        }

        Assert.Equal(
            5,
            publisher.PublishCallCount);

        await using var verificationContext =
            Fixture.CreateDbContext();

        var persisted =
            await verificationContext.OutboxMessages
                .AsNoTracking()
                .SingleAsync(
                    message =>
                        message.Id == messageId);

        Assert.Null(
            persisted.PublishedAt);

        Assert.Equal(
            5,
            persisted.AttemptCount);

        Assert.Null(
            persisted.NextAttemptAt);

        Assert.Equal(
            InitialTime
                .AddSeconds(
                    5 + 15 + 45 + 135),
            persisted.DeadLetteredAt);

        Assert.NotNull(
            persisted.LastError);

        Assert.Contains(
            "Permanent broker failure.",
            persisted.LastError);

        timeProvider.Advance(
            TimeSpan.FromDays(
                1));

        await using var finalContext =
            Fixture.CreateDbContext();

        var finalProcessor =
            new OutboxProcessor(
                finalContext,
                publisher,
                timeProvider);

        var finalResult =
            await finalProcessor.ProcessBatchAsync(
                batchSize: 10);

        Assert.Equal(
            0,
            finalResult.Candidates);

        Assert.Equal(
            5,
            publisher.PublishCallCount);
    }

    [Fact]
    public async Task ProcessBatchAsync_MessageInBackoff_DoesNotBlockNewMessage()
    {
        var timeProvider =
            new ManualTimeProvider(
                InitialTime);

        var delayedMessageId =
            await SeedOutboxMessageAsync(
                InitialTime);

        var failingPublisher =
            new FakeIntegrationEventPublisher(
                failure:
                    new InvalidOperationException(
                        "Temporary failure."));

        await using (
            var firstContext =
                Fixture.CreateDbContext())
        {
            var firstProcessor =
                new OutboxProcessor(
                    firstContext,
                    failingPublisher,
                    timeProvider);

            var firstResult =
                await firstProcessor.ProcessBatchAsync(
                    batchSize: 10);

            Assert.Equal(
                1,
                firstResult.Failed);
        }

        var newMessageId =
            await SeedOutboxMessageAsync(
                InitialTime.AddSeconds(
                    1));

        var successfulPublisher =
            new FakeIntegrationEventPublisher();

        await using (
            var secondContext =
                Fixture.CreateDbContext())
        {
            var secondProcessor =
                new OutboxProcessor(
                    secondContext,
                    successfulPublisher,
                    timeProvider);

            var secondResult =
                await secondProcessor.ProcessBatchAsync(
                    batchSize: 10);

            Assert.Equal(
                1,
                secondResult.Candidates);

            Assert.Equal(
                1,
                secondResult.Published);

            Assert.Equal(
                1,
                successfulPublisher.PublishCallCount);

            Assert.Equal(
                newMessageId,
                Assert.Single(
                    successfulPublisher.PublishedMessages).MessageId);
        }

        await using var verificationContext =
            Fixture.CreateDbContext();

        var delayedMessage =
            await verificationContext.OutboxMessages
                .AsNoTracking()
                .SingleAsync(
                    message =>
                        message.Id == delayedMessageId);

        var newMessage =
            await verificationContext.OutboxMessages
                .AsNoTracking()
                .SingleAsync(
                    message =>
                        message.Id == newMessageId);

        Assert.Null(
            delayedMessage.PublishedAt);

        Assert.NotNull(
            delayedMessage.NextAttemptAt);

        Assert.NotNull(
            newMessage.PublishedAt);
    }

    private async Task ProcessFailureAsync(
        FakeIntegrationEventPublisher publisher,
        ManualTimeProvider timeProvider)
    {
        await using var dbContext =
            Fixture.CreateDbContext();

        var processor =
            new OutboxProcessor(
                dbContext,
                publisher,
                timeProvider);

        var result =
            await processor.ProcessBatchAsync(
                batchSize: 10);

        Assert.Equal(
            1,
            result.Candidates);

        Assert.Equal(
            0,
            result.Published);

        Assert.Equal(
            1,
            result.Failed);

        Assert.Equal(
            0,
            result.DeadLettered);
    }

    private async Task<Guid> SeedOutboxMessageAsync(
        DateTimeOffset occurredAt)
    {
        await using var dbContext =
            Fixture.CreateDbContext();

        var message =
            new OutboxMessage
            {
                Id =
                    Guid.NewGuid(),

                EventType =
                    "transfer.completed.v1",

                AggregateId =
                    Guid.NewGuid(),

                Payload =
                    """
                    {
                      "eventId": "11111111-1111-1111-1111-111111111111",
                      "transferId": "22222222-2222-2222-2222-222222222222",
                      "sourceAccountId": "33333333-3333-3333-3333-333333333333",
                      "destinationAccountId": "44444444-4444-4444-4444-444444444444",
                      "amount": 250.00,
                      "occurredAt": "2026-09-11T05:30:00Z"
                    }
                    """,

                OccurredAt =
                    occurredAt,

                CreatedAt =
                    occurredAt,

                PublishedAt =
                    null,

                NextAttemptAt =
                    null,

                DeadLetteredAt =
                    null,

                AttemptCount =
                    0,

                LastError =
                    null
            };

        await dbContext.OutboxMessages.AddAsync(
            message);

        await dbContext.SaveChangesAsync();

        return message.Id;
    }

    private sealed class FakeIntegrationEventPublisher
        : IIntegrationEventPublisher
    {
        private readonly Exception? _failure;

        public FakeIntegrationEventPublisher(
            Exception? failure = null)
        {
            _failure =
                failure;
        }

        public int PublishCallCount { get; private set; }

        public List<PublishedMessage> PublishedMessages { get; } =
            [];

        public Task PublishAsync(
            Guid messageId,
            string eventType,
            Guid aggregateId,
            string payload,
            CancellationToken cancellationToken = default)
        {
            PublishCallCount++;

            if (_failure is not null)
            {
                throw _failure;
            }

            PublishedMessages.Add(
                new PublishedMessage(
                    messageId,
                    eventType,
                    aggregateId,
                    payload));

            return Task.CompletedTask;
        }
    }

    private sealed record PublishedMessage(
        Guid MessageId,
        string EventType,
        Guid AggregateId,
        string Payload);

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
