using System.Diagnostics;
using FluxPay.Application.Abstractions.Messaging;
using FluxPay.Infrastructure.Observability;
using FluxPay.Infrastructure.Persistence.Outbox;
using FluxPay.IntegrationTests.Infrastructure;

namespace FluxPay.IntegrationTests.Outbox;

public sealed class OutboxTracePropagationIntegrationTests
    : PostgreSqlIntegrationTestBase
{
    private static readonly DateTimeOffset OccurredAt =
        new(
            2026,
            9,
            11,
            8,
            30,
            0,
            TimeSpan.Zero);

    public OutboxTracePropagationIntegrationTests(
        PostgreSqlIntegrationFixture fixture)
        : base(
            fixture)
    {
    }

    [Fact]
    public async Task ProcessBatchAsync_WithPersistedTraceContext_CreatesProducerSpanAsChild()
    {
        var parentTraceId =
            ActivityTraceId.CreateRandom();

        var parentSpanId =
            ActivitySpanId.CreateRandom();

        var traceParent =
            $"00-{parentTraceId}-{parentSpanId}-01";

        const string traceState =
            "fluxpay=test";

        var eventId =
            Guid.NewGuid();

        var aggregateId =
            Guid.NewGuid();

        await SeedOutboxMessageAsync(
            eventId,
            aggregateId,
            traceParent,
            traceState);

        var publisher =
            new CapturingIntegrationEventPublisher();

        using var listener =
            new ActivityListener
            {
                ShouldListenTo =
                    source =>
                        source.Name
                        == MessagingActivitySource.Name,

                Sample =
                    (
                        ref ActivityCreationOptions<ActivityContext>
                            _
                    ) =>
                        ActivitySamplingResult.AllDataAndRecorded
            };

        ActivitySource.AddActivityListener(
            listener);

        await using var dbContext =
            Fixture.CreateDbContext();

        var processor =
            new OutboxProcessor(
                dbContext,
                publisher,
                TimeProvider.System);

        var result =
            await processor.ProcessBatchAsync(
                batchSize:
                    10);

        Assert.Equal(
            1,
            result.Candidates);

        Assert.Equal(
            1,
            result.Published);

        Assert.Equal(
            1,
            publisher.PublishCallCount);

        Assert.NotNull(
            publisher.ActivityContext);

        Assert.Equal(
            parentTraceId,
            publisher.ActivityContext.Value.TraceId);

        Assert.NotEqual(
            parentSpanId,
            publisher.ActivityContext.Value.SpanId);

        Assert.Equal(
            parentSpanId,
            publisher.ParentSpanId);

        Assert.Equal(
            ActivityKind.Producer,
            publisher.ActivityKind);

        Assert.Equal(
            traceState,
            publisher.TraceState);

        Assert.Equal(
            "transfer.completed.v1 publish",
            publisher.ActivityDisplayName);
    }

    private async Task SeedOutboxMessageAsync(
        Guid eventId,
        Guid aggregateId,
        string traceParent,
        string traceState)
    {
        await using var dbContext =
            Fixture.CreateDbContext();

        var message =
            new OutboxMessage
            {
                Id =
                    eventId,

                EventType =
                    "transfer.completed.v1",

                AggregateId =
                    aggregateId,

                Payload =
                    """
                    {
                      "eventId": "11111111-1111-1111-1111-111111111111",
                      "transferId": "22222222-2222-2222-2222-222222222222",
                      "sourceAccountId": "33333333-3333-3333-3333-333333333333",
                      "destinationAccountId": "44444444-4444-4444-4444-444444444444",
                      "amount": 250.00,
                      "occurredAt": "2026-09-11T08:30:00Z"
                    }
                    """,

                TraceParent =
                    traceParent,

                TraceState =
                    traceState,

                OccurredAt =
                    OccurredAt,

                CreatedAt =
                    OccurredAt,

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
    }

    private sealed class CapturingIntegrationEventPublisher
        : IIntegrationEventPublisher
    {
        public int PublishCallCount { get; private set; }

        public ActivityContext? ActivityContext { get; private set; }

        public ActivitySpanId? ParentSpanId { get; private set; }

        public ActivityKind? ActivityKind { get; private set; }

        public string? TraceState { get; private set; }

        public string? ActivityDisplayName { get; private set; }

        public Task PublishAsync(
            Guid messageId,
            string eventType,
            Guid aggregateId,
            string payload,
            CancellationToken cancellationToken = default)
        {
            PublishCallCount++;

            var activity =
                Activity.Current;

            Assert.NotNull(
                activity);

            ActivityContext =
                activity.Context;

            ParentSpanId =
                activity.ParentSpanId;

            ActivityKind =
                activity.Kind;

            TraceState =
                activity.TraceStateString;

            ActivityDisplayName =
                activity.DisplayName;

            return Task.CompletedTask;
        }
    }
}
