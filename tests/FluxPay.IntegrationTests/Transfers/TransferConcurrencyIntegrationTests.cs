using System.Collections.Concurrent;
using FluxPay.Application.Transfers.ExecuteTransfer;
using FluxPay.Domain.Accounts;
using FluxPay.Domain.Common;
using FluxPay.Domain.Transfers;
using FluxPay.Domain.ValueObjects;
using FluxPay.Infrastructure.Persistence;
using FluxPay.Infrastructure.Persistence.Idempotency;
using FluxPay.Infrastructure.Persistence.Repositories;
using FluxPay.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.IntegrationTests.Transfers;

public sealed class TransferConcurrencyIntegrationTests
    : PostgreSqlIntegrationTestBase
{
    private const int ConcurrentTransfers =
        100;

    private const decimal InitialSourceBalance =
        1000.00m;

    private const decimal TransferAmount =
        100.00m;

    public TransferConcurrencyIntegrationTests(
        PostgreSqlIntegrationFixture fixture)
        : base(
            fixture)
    {
    }

    [Fact]
    public async Task ExecuteTransfer_WithOneHundredConcurrentRequests_PreservesMoneyAndAllowsExactlyTenTransfers()
    {
        var seededAccounts =
            await SeedAccountsAsync();

        var startSignal =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var readyWorkers =
            0;

        var successfulTransferIds =
            new ConcurrentBag<Guid>();

        var failures =
            new ConcurrentBag<Exception>();

        var tasks =
            seededAccounts.DestinationAccountIds
                .Select(
                    async destinationAccountId =>
                    {
                        var ready =
                            Interlocked.Increment(
                                ref readyWorkers);

                        if (ready == ConcurrentTransfers)
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
                                        Guid.NewGuid(),
                                        seededAccounts.SourceAccountId,
                                        destinationAccountId,
                                        TransferAmount));

                            successfulTransferIds.Add(
                                result.Id);
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
            10,
            successfulTransferIds.Count);

        Assert.Equal(
            90,
            failures.Count);

        Assert.All(
            failures,
            exception =>
                Assert.IsType<InsufficientFundsException>(
                    exception));

        Assert.Equal(
            10,
            successfulTransferIds
                .Distinct()
                .Count());

        await using var verificationContext =
            Fixture.CreateDbContext();

        var source =
            await verificationContext.Accounts
                .AsNoTracking()
                .SingleAsync(
                    account =>
                        account.Id
                        == seededAccounts.SourceAccountId);

        var destinations =
            await verificationContext.Accounts
                .AsNoTracking()
                .Where(
                    account =>
                        seededAccounts.DestinationAccountIds
                            .Contains(
                                account.Id))
                .ToListAsync();

        var transfers =
            await verificationContext.Transfers
                .AsNoTracking()
                .Where(
                    transfer =>
                        transfer.SourceAccountId
                        == seededAccounts.SourceAccountId)
                .ToListAsync();

        var idempotencyRecords =
            await verificationContext
                .Set<TransferIdempotencyRecord>()
                .AsNoTracking()
                .Where(
                    record =>
                        record.SourceAccountId
                        == seededAccounts.SourceAccountId)
                .ToListAsync();

        var destinationBalanceTotal =
            destinations.Sum(
                account =>
                    account.Balance.Amount);

        var totalMoney =
            source.Balance.Amount
            + destinationBalanceTotal;

        var creditedDestinations =
            destinations.Count(
                account =>
                    account.Balance.Amount
                    == TransferAmount);

        var untouchedDestinations =
            destinations.Count(
                account =>
                    account.Balance.Amount
                    == 0.00m);

        Assert.Equal(
            0.00m,
            source.Balance.Amount);

        Assert.Equal(
            1000.00m,
            destinationBalanceTotal);

        Assert.Equal(
            InitialSourceBalance,
            totalMoney);

        Assert.Equal(
            10,
            creditedDestinations);

        Assert.Equal(
            90,
            untouchedDestinations);

        Assert.Equal(
            10,
            transfers.Count);

        Assert.All(
            transfers,
            transfer =>
            {
                Assert.Equal(
                    TransferStatus.Completed,
                    transfer.Status);

                Assert.Equal(
                    TransferAmount,
                    transfer.Amount.Amount);
            });

        Assert.Equal(
            10,
            idempotencyRecords.Count);

        Assert.All(
            idempotencyRecords,
            record =>
            {
                Assert.NotNull(
                    record.TransferId);

                Assert.NotNull(
                    record.CompletedAt);

                Assert.Equal(
                    TransferAmount,
                    record.Amount);
            });

        var persistedTransferIds =
            transfers
                .Select(
                    transfer =>
                        transfer.Id)
                .OrderBy(
                    id =>
                        id)
                .ToArray();

        var returnedTransferIds =
            successfulTransferIds
                .OrderBy(
                    id =>
                        id)
                .ToArray();

        Assert.Equal(
            persistedTransferIds,
            returnedTransferIds);
    }

    private async Task<SeededAccounts> SeedAccountsAsync()
    {
        await using var dbContext =
            Fixture.CreateDbContext();

        var occurredAt =
            DateTimeOffset.UtcNow;

        var source =
            Account.Create(
                "CONC-SOURCE",
                "Concurrency Source",
                new Money(
                    InitialSourceBalance),
                occurredAt);

        var destinations =
            Enumerable
                .Range(
                    1,
                    ConcurrentTransfers)
                .Select(
                    index =>
                        Account.Create(
                            $"CONC-DEST-{index:000}",
                            $"Concurrency Destination {index:000}",
                            new Money(
                                0.00m),
                            occurredAt))
                .ToArray();

        await dbContext.Accounts.AddAsync(
            source);

        await dbContext.Accounts.AddRangeAsync(
            destinations);

        await dbContext.SaveChangesAsync();

        return new SeededAccounts(
            source.Id,
            destinations
                .Select(
                    account =>
                        account.Id)
                .ToArray());
    }

    private static ExecuteTransferHandler CreateHandler(
        FluxPayDbContext dbContext)
    {
        return new ExecuteTransferHandler(
            new EfTransferAccountRepository(
                dbContext),
            new EfTransferRepository(
                dbContext),
            new EfTransferIdempotencyRepository(
                dbContext),
            new EfUnitOfWork(
                dbContext),
            new EfTransactionManager(
                dbContext),
            TimeProvider.System);
    }

    private sealed record SeededAccounts(
        Guid SourceAccountId,
        Guid[] DestinationAccountIds);
}
