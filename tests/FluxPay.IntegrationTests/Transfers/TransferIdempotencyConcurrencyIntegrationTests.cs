using System.Collections.Concurrent;
using FluxPay.Application.Transfers.Events;
using FluxPay.Application.Transfers.ExecuteTransfer;
using FluxPay.Domain.Accounts;
using FluxPay.Domain.Transfers;
using FluxPay.Domain.ValueObjects;
using FluxPay.Infrastructure.Persistence;
using FluxPay.Infrastructure.Persistence.Idempotency;
using FluxPay.Infrastructure.Persistence.Outbox;
using FluxPay.Infrastructure.Persistence.Repositories;
using FluxPay.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.IntegrationTests.Transfers;

public sealed class TransferIdempotencyConcurrencyIntegrationTests
    : PostgreSqlIntegrationTestBase
{
    private const int ConcurrentRequests =
        100;

    private const decimal InitialSourceBalance =
        1000.00m;

    private const decimal InitialDestinationBalance =
        100.00m;

    private const decimal TransferAmount =
        250.00m;

    public TransferIdempotencyConcurrencyIntegrationTests(
        PostgreSqlIntegrationFixture fixture)
        : base(
            fixture)
    {
    }

    [Fact]
    public async Task ExecuteTransfer_WithOneHundredConcurrentRequestsUsingSameKey_ExecutesOnceAndCreatesOneOutboxMessage()
    {
        var accounts =
            await SeedAccountsAsync();

        var idempotencyKey =
            Guid.NewGuid();

        var startSignal =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var readyWorkers =
            0;

        var results =
            new ConcurrentBag<ExecuteTransferResult>();

        var failures =
            new ConcurrentBag<Exception>();

        var tasks =
            Enumerable
                .Range(
                    1,
                    ConcurrentRequests)
                .Select(
                    async _ =>
                    {
                        var ready =
                            Interlocked.Increment(
                                ref readyWorkers);

                        if (ready == ConcurrentRequests)
                        {
                            startSignal.TrySetResult(
                                true);
                        }

                        await startSignal.Task;

                        try
                        {
                            await using var dbContext =
                                Fixture.CreateDbContext();

                            var handler =
                                CreateHandler(
                                    dbContext);

                            var result =
                                await handler.HandleAsync(
                                    new ExecuteTransferCommand(
                                        idempotencyKey,
                                        accounts.SourceAccountId,
                                        accounts.DestinationAccountId,
                                        TransferAmount));

                            results.Add(
                                result);
                        }
                        catch (Exception exception)
                        {
                            failures.Add(
                                exception);
                        }
                    })
                .ToArray();

        await Task.WhenAll(
            tasks);

        Assert.Equal(
            ConcurrentRequests,
            results.Count);

        Assert.Empty(
            failures);

        var distinctTransferIds =
            results
                .Select(
                    result =>
                        result.Id)
                .Distinct()
                .ToArray();

        Assert.Single(
            distinctTransferIds);

        var originalExecutions =
            results.Count(
                result =>
                    !result.IsReplay);

        var replayExecutions =
            results.Count(
                result =>
                    result.IsReplay);

        Assert.Equal(
            1,
            originalExecutions);

        Assert.Equal(
            99,
            replayExecutions);

        await using var verificationContext =
            Fixture.CreateDbContext();

        var source =
            await verificationContext.Accounts
                .AsNoTracking()
                .SingleAsync(
                    account =>
                        account.Id
                        == accounts.SourceAccountId);

        var destination =
            await verificationContext.Accounts
                .AsNoTracking()
                .SingleAsync(
                    account =>
                        account.Id
                        == accounts.DestinationAccountId);

        var transfers =
            await verificationContext.Transfers
                .AsNoTracking()
                .Where(
                    transfer =>
                        transfer.SourceAccountId
                            == accounts.SourceAccountId
                        && transfer.DestinationAccountId
                            == accounts.DestinationAccountId)
                .ToListAsync();

        var idempotencyRecords =
            await verificationContext
                .Set<TransferIdempotencyRecord>()
                .AsNoTracking()
                .Where(
                    record =>
                        record.IdempotencyKey
                        == idempotencyKey)
                .ToListAsync();

        var outboxMessages =
            await verificationContext.OutboxMessages
                .AsNoTracking()
                .Where(
                    message =>
                        message.EventType
                        == TransferCompletedIntegrationEvent.EventType)
                .ToListAsync();

        Assert.Equal(
            750.00m,
            source.Balance.Amount);

        Assert.Equal(
            350.00m,
            destination.Balance.Amount);

        Assert.Equal(
            1100.00m,
            source.Balance.Amount
            + destination.Balance.Amount);

        var transfer =
            Assert.Single(
                transfers);

        Assert.Equal(
            TransferStatus.Completed,
            transfer.Status);

        Assert.Equal(
            TransferAmount,
            transfer.Amount.Amount);

        Assert.Equal(
            distinctTransferIds.Single(),
            transfer.Id);

        var idempotencyRecord =
            Assert.Single(
                idempotencyRecords);

        Assert.Equal(
            idempotencyKey,
            idempotencyRecord.IdempotencyKey);

        Assert.Equal(
            TransferAmount,
            idempotencyRecord.Amount);

        Assert.Equal(
            transfer.Id,
            idempotencyRecord.TransferId);

        Assert.NotNull(
            idempotencyRecord.CompletedAt);

        var outboxMessage =
            Assert.Single(
                outboxMessages);

        Assert.Equal(
            transfer.Id,
            outboxMessage.AggregateId);

        Assert.Equal(
            TransferCompletedIntegrationEvent.EventType,
            outboxMessage.EventType);

        Assert.Null(
            outboxMessage.PublishedAt);

        Assert.Equal(
            0,
            outboxMessage.AttemptCount);

        Assert.Null(
            outboxMessage.LastError);

        Assert.All(
            results,
            result =>
            {
                Assert.Equal(
                    transfer.Id,
                    result.Id);

                Assert.Equal(
                    TransferStatus.Completed,
                    result.Status);

                Assert.Equal(
                    TransferAmount,
                    result.Amount);
            });
    }

    private async Task<SeededAccounts> SeedAccountsAsync()
    {
        await using var dbContext =
            Fixture.CreateDbContext();

        var occurredAt =
            DateTimeOffset.UtcNow;

        var source =
            Account.Create(
                "IDEMP-CONC-SOURCE",
                "Idempotency Concurrency Source",
                new Money(
                    InitialSourceBalance),
                occurredAt);

        var destination =
            Account.Create(
                "IDEMP-CONC-DESTINATION",
                "Idempotency Concurrency Destination",
                new Money(
                    InitialDestinationBalance),
                occurredAt);

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
            TimeProvider.System;

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
}
