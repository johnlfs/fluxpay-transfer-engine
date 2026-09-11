using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Transfers.ExecuteTransfer;
using FluxPay.Domain.Accounts;
using FluxPay.Domain.ValueObjects;
using FluxPay.Infrastructure.Persistence;
using FluxPay.Infrastructure.Persistence.Outbox;
using FluxPay.Infrastructure.Persistence.Repositories;
using FluxPay.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.IntegrationTests.Transfers;

public sealed class TransferTimestampPrecisionIntegrationTests
    : PostgreSqlIntegrationTestBase
{
    private static readonly DateTimeOffset SubMicrosecondUtcNow =
        new DateTimeOffset(
            2026,
            9,
            11,
            7,
            9,
            9,
            TimeSpan.Zero)
        .AddTicks(
            2_607_132);

    private static readonly DateTimeOffset ExpectedNormalizedUtcNow =
        new DateTimeOffset(
            2026,
            9,
            11,
            7,
            9,
            9,
            TimeSpan.Zero)
        .AddTicks(
            2_607_130);

    public TransferTimestampPrecisionIntegrationTests(
        PostgreSqlIntegrationFixture fixture)
        : base(
            fixture)
    {
    }

    [Fact]
    public async Task ExecuteTransfer_WithSubMicrosecondTimestamp_FirstResultPersistedTransferAndReplayAreIdentical()
    {
        var accounts =
            await SeedAccountsAsync();

        var idempotencyKey =
            Guid.NewGuid();

        ExecuteTransferResult firstResult;

        await using (
            var firstContext =
                Fixture.CreateDbContext())
        {
            var handler =
                CreateHandler(
                    firstContext);

            firstResult =
                await handler.HandleAsync(
                    new ExecuteTransferCommand(
                        idempotencyKey,
                        accounts.SourceAccountId,
                        accounts.DestinationAccountId,
                        125.50m));
        }

        Assert.False(
            firstResult.IsReplay);

        Assert.Equal(
            ExpectedNormalizedUtcNow,
            firstResult.CreatedAt);

        Assert.True(
            firstResult.FinalizedAt.HasValue);

        Assert.Equal(
            ExpectedNormalizedUtcNow,
            firstResult.FinalizedAt.Value);

        Assert.Equal(
            0,
            firstResult.CreatedAt.Ticks % 10);

        Assert.Equal(
            0,
            firstResult.FinalizedAt.Value.Ticks % 10);

        await using (
            var verificationContext =
                Fixture.CreateDbContext())
        {
            var persistedTransfer =
                await verificationContext.Transfers
                    .AsNoTracking()
                    .SingleAsync(
                        transfer =>
                            transfer.Id
                            == firstResult.Id);

            Assert.Equal(
                firstResult.CreatedAt,
                persistedTransfer.CreatedAt);

            Assert.Equal(
                firstResult.FinalizedAt,
                persistedTransfer.FinalizedAt);

            Assert.Equal(
                ExpectedNormalizedUtcNow,
                persistedTransfer.CreatedAt);

            Assert.Equal(
                ExpectedNormalizedUtcNow,
                persistedTransfer.FinalizedAt);

            Assert.Equal(
                0,
                persistedTransfer.CreatedAt.Ticks % 10);

            Assert.True(
                persistedTransfer.FinalizedAt.HasValue);

            Assert.Equal(
                0,
                persistedTransfer.FinalizedAt.Value.Ticks % 10);

            var outboxMessage =
                await verificationContext.OutboxMessages
                    .AsNoTracking()
                    .SingleAsync(
                        message =>
                            message.AggregateId
                            == firstResult.Id);

            Assert.Equal(
                ExpectedNormalizedUtcNow,
                outboxMessage.OccurredAt);

            Assert.Equal(
                0,
                outboxMessage.OccurredAt.Ticks % 10);

            Assert.Equal(
                0,
                outboxMessage.CreatedAt.Ticks % 10);
        }

        ExecuteTransferResult replayResult;

        await using (
            var replayContext =
                Fixture.CreateDbContext())
        {
            var handler =
                CreateHandler(
                    replayContext);

            replayResult =
                await handler.HandleAsync(
                    new ExecuteTransferCommand(
                        idempotencyKey,
                        accounts.SourceAccountId,
                        accounts.DestinationAccountId,
                        125.50m));
        }

        Assert.True(
            replayResult.IsReplay);

        Assert.Equal(
            firstResult.Id,
            replayResult.Id);

        Assert.Equal(
            firstResult.SourceAccountId,
            replayResult.SourceAccountId);

        Assert.Equal(
            firstResult.DestinationAccountId,
            replayResult.DestinationAccountId);

        Assert.Equal(
            firstResult.Amount,
            replayResult.Amount);

        Assert.Equal(
            firstResult.Status,
            replayResult.Status);

        Assert.Equal(
            firstResult.CreatedAt,
            replayResult.CreatedAt);

        Assert.Equal(
            firstResult.FinalizedAt,
            replayResult.FinalizedAt);

        Assert.Equal(
            ExpectedNormalizedUtcNow,
            replayResult.CreatedAt);

        Assert.True(
            replayResult.FinalizedAt.HasValue);

        Assert.Equal(
            ExpectedNormalizedUtcNow,
            replayResult.FinalizedAt.Value);
    }

    private async Task<SeededAccounts> SeedAccountsAsync()
    {
        await using var dbContext =
            Fixture.CreateDbContext();

        var accountCreatedAt =
            new DateTimeOffset(
                2026,
                9,
                10,
                7,
                9,
                9,
                TimeSpan.Zero);

        var source =
            Account.Create(
                "TIMESTAMP-SOURCE",
                "Timestamp Source",
                new Money(
                    1000.00m),
                accountCreatedAt);

        var destination =
            Account.Create(
                "TIMESTAMP-DESTINATION",
                "Timestamp Destination",
                new Money(
                    100.00m),
                accountCreatedAt);

        await dbContext.Accounts.AddRangeAsync(
            source,
            destination);

        await dbContext.SaveChangesAsync();

        return new SeededAccounts(
            source.Id,
            destination.Id);
    }

    private static ExecuteTransferHandler CreateHandler(
        FluxPayDbContext dbContext)
    {
        var timeProvider =
            new FixedTimeProvider(
                SubMicrosecondUtcNow);

        return new ExecuteTransferHandler(
            new EfTransferAccountRepository(
                dbContext),
            new EfTransferRepository(
                dbContext),
            new EfTransferIdempotencyRepository(
                dbContext),
            new EfOutboxWriter(
                dbContext,
                timeProvider),
            new EfUnitOfWork(
                dbContext),
            new EfTransactionManager(
                dbContext),
            timeProvider);
    }

    private sealed record SeededAccounts(
        Guid SourceAccountId,
        Guid DestinationAccountId);

    private sealed class FixedTimeProvider
        : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow =
                utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
