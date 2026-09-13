using System.Diagnostics;
using FluxPay.Infrastructure.Persistence.Outbox;
using FluxPay.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.IntegrationTests.Outbox;

public sealed class OutboxTraceContextIntegrationTests
    : PostgreSqlIntegrationTestBase
{
    private static readonly DateTimeOffset OccurredAt =
        new(
            2026,
            9,
            11,
            8,
            15,
            0,
            TimeSpan.Zero);

    public OutboxTraceContextIntegrationTests(
        PostgreSqlIntegrationFixture fixture)
        : base(
            fixture)
    {
    }

    [Fact]
    public async Task AddAsync_WithActiveW3CActivity_PersistsTraceParentAndTraceState()
    {
        var previousActivity =
            Activity.Current;

        using var activity =
            new Activity(
                "fluxpay.integration-test");

        activity.SetIdFormat(
            ActivityIdFormat.W3C);

        activity.TraceStateString =
            "fluxpay=test";

        activity.Start();

        try
        {
            Assert.Equal(
                ActivityIdFormat.W3C,
                activity.IdFormat);

            Assert.NotNull(
                activity.Id);

            var expectedTraceParent =
                activity.Id!;

            var expectedTraceState =
                activity.TraceStateString;

            var eventId =
                Guid.NewGuid();

            await using (
                var dbContext =
                    Fixture.CreateDbContext())
            {
                await using var transaction =
                    await dbContext.Database
                        .BeginTransactionAsync();

                var writer =
                    new EfOutboxWriter(
                        dbContext,
                        TimeProvider.System);

                await writer.AddAsync(
                    eventId,
                    "transfer.completed.v1",
                    Guid.NewGuid(),
                    new TestPayload(
                        "trace-context"),
                    OccurredAt);

                await dbContext.SaveChangesAsync();

                await transaction.CommitAsync();
            }

            await using var verificationContext =
                Fixture.CreateDbContext();

            var persisted =
                await verificationContext.OutboxMessages
                    .AsNoTracking()
                    .SingleAsync(
                        message =>
                            message.Id == eventId);

            Assert.Equal(
                expectedTraceParent,
                persisted.TraceParent);

            Assert.Equal(
                expectedTraceState,
                persisted.TraceState);

            var parsed =
                ActivityContext.TryParse(
                    persisted.TraceParent,
                    persisted.TraceState,
                    isRemote:
                        false,
                    out var persistedContext);

            Assert.True(
                parsed);

            Assert.Equal(
                activity.TraceId,
                persistedContext.TraceId);

            Assert.Equal(
                activity.SpanId,
                persistedContext.SpanId);

            Assert.Equal(
                activity.ActivityTraceFlags,
                persistedContext.TraceFlags);

            Assert.Equal(
                expectedTraceState,
                persistedContext.TraceState);
        }
        finally
        {
            activity.Stop();

            Activity.Current =
                previousActivity;
        }
    }

    [Fact]
    public async Task AddAsync_WithOversizedTraceState_DropsTraceStateAndPersistsTraceParent()
    {
        var previousActivity =
            Activity.Current;

        using var activity =
            new Activity(
                "fluxpay.oversized-tracestate-test");

        activity.SetIdFormat(
            ActivityIdFormat.W3C);

        activity.TraceStateString =
            new string(
                'a',
                513);

        activity.Start();

        try
        {
            var expectedTraceParent =
                activity.Id;

            Assert.NotNull(
                expectedTraceParent);

            var eventId =
                Guid.NewGuid();

            await using (
                var dbContext =
                    Fixture.CreateDbContext())
            {
                await using var transaction =
                    await dbContext.Database
                        .BeginTransactionAsync();

                var writer =
                    new EfOutboxWriter(
                        dbContext,
                        TimeProvider.System);

                await writer.AddAsync(
                    eventId,
                    "transfer.completed.v1",
                    Guid.NewGuid(),
                    new TestPayload(
                        "oversized-tracestate"),
                    OccurredAt);

                await dbContext.SaveChangesAsync();

                await transaction.CommitAsync();
            }

            await using var verificationContext =
                Fixture.CreateDbContext();

            var persisted =
                await verificationContext
                    .OutboxMessages
                    .AsNoTracking()
                    .SingleAsync(
                        message =>
                            message.Id
                            == eventId);

            Assert.Equal(
                expectedTraceParent,
                persisted.TraceParent);

            Assert.Null(
                persisted.TraceState);
        }
        finally
        {
            activity.Stop();

            Activity.Current =
                previousActivity;
        }
    }

    [Fact]
    public async Task AddAsync_WithoutActiveActivity_PersistsNullTraceContext()
    {
        var previousActivity =
            Activity.Current;

        Activity.Current =
            null;

        try
        {
            var eventId =
                Guid.NewGuid();

            await using (
                var dbContext =
                    Fixture.CreateDbContext())
            {
                await using var transaction =
                    await dbContext.Database
                        .BeginTransactionAsync();

                var writer =
                    new EfOutboxWriter(
                        dbContext,
                        TimeProvider.System);

                await writer.AddAsync(
                    eventId,
                    "transfer.completed.v1",
                    Guid.NewGuid(),
                    new TestPayload(
                        "no-trace-context"),
                    OccurredAt);

                await dbContext.SaveChangesAsync();

                await transaction.CommitAsync();
            }

            await using var verificationContext =
                Fixture.CreateDbContext();

            var persisted =
                await verificationContext.OutboxMessages
                    .AsNoTracking()
                    .SingleAsync(
                        message =>
                            message.Id == eventId);

            Assert.Null(
                persisted.TraceParent);

            Assert.Null(
                persisted.TraceState);
        }
        finally
        {
            Activity.Current =
                previousActivity;
        }
    }

    private sealed record TestPayload(
        string Value);
}
