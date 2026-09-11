using FluxPay.Application.Abstractions.Messaging;
using FluxPay.Infrastructure.Persistence.Outbox;
using FluxPay.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.IntegrationTests.Outbox;

public sealed class OutboxProcessorIntegrationTests
    : PostgreSqlIntegrationTestBase
{
    public OutboxProcessorIntegrationTests(
        PostgreSqlIntegrationFixture fixture)
        : base(
            fixture)
    {
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenPublishSucceeds_MarksMessageAsPublished()
    {
        var messageId =
            await SeedOutboxMessageAsync();

        var publisher =
            new FakeIntegrationEventPublisher();

        await using var dbContext =
            Fixture.CreateDbContext();

        var processor =
            new OutboxProcessor(
                dbContext,
                publisher,
                TimeProvider.System);

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
            result.Skipped);

        Assert.Equal(
            1,
            publisher.PublishCallCount);

        var publishedMessage =
            Assert.Single(
                publisher.PublishedMessages);

        Assert.Equal(
            messageId,
            publishedMessage.MessageId);

        await using var verificationContext =
            Fixture.CreateDbContext();

        var persisted =
            await verificationContext.OutboxMessages
                .AsNoTracking()
                .SingleAsync(
                    message =>
                        message.Id == messageId);

        Assert.NotNull(
            persisted.PublishedAt);

        Assert.Equal(
            1,
            persisted.AttemptCount);

        Assert.Null(
            persisted.LastError);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenPublishFails_KeepsMessagePendingAndStoresFailure()
    {
        var messageId =
            await SeedOutboxMessageAsync();

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
                TimeProvider.System);

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

        Assert.Null(
            persisted.PublishedAt);

        Assert.Equal(
            1,
            persisted.AttemptCount);

        Assert.NotNull(
            persisted.LastError);

        Assert.Contains(
            "InvalidOperationException",
            persisted.LastError);

        Assert.Contains(
            "Broker unavailable.",
            persisted.LastError);
    }

    [Fact]
    public async Task ProcessBatchAsync_AfterFailure_RetriesAndEventuallyMarksMessageAsPublished()
    {
        var messageId =
            await SeedOutboxMessageAsync();

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
                    TimeProvider.System);

            var firstResult =
                await firstProcessor.ProcessBatchAsync(
                    batchSize: 10);

            Assert.Equal(
                1,
                firstResult.Failed);
        }

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
                    TimeProvider.System);

            var retryResult =
                await retryProcessor.ProcessBatchAsync(
                    batchSize: 10);

            Assert.Equal(
                1,
                retryResult.Published);

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

        Assert.NotNull(
            persisted.PublishedAt);

        Assert.Equal(
            2,
            persisted.AttemptCount);

        Assert.Null(
            persisted.LastError);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenThereAreNoPendingMessages_DoesNothing()
    {
        var publisher =
            new FakeIntegrationEventPublisher();

        await using var dbContext =
            Fixture.CreateDbContext();

        var processor =
            new OutboxProcessor(
                dbContext,
                publisher,
                TimeProvider.System);

        var result =
            await processor.ProcessBatchAsync(
                batchSize: 10);

        Assert.Equal(
            0,
            result.Candidates);

        Assert.Equal(
            0,
            result.Published);

        Assert.Equal(
            0,
            result.Failed);

        Assert.Equal(
            0,
            result.Skipped);

        Assert.Equal(
            0,
            publisher.PublishCallCount);
    }

    private async Task<Guid> SeedOutboxMessageAsync()
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
                      "occurredAt": "2026-09-11T04:30:00Z"
                    }
                    """,

                OccurredAt =
                    DateTimeOffset.UtcNow,

                CreatedAt =
                    DateTimeOffset.UtcNow,

                PublishedAt =
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
}
