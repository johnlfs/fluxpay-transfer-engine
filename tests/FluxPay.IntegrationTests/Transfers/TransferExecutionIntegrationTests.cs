using FluxPay.Application.Transfers.Exceptions;
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

public sealed class TransferExecutionIntegrationTests
    : PostgreSqlIntegrationTestBase
{
    public TransferExecutionIntegrationTests(
        PostgreSqlIntegrationFixture fixture)
        : base(
            fixture)
    {
    }

    [Fact]
    public async Task ExecuteTransfer_WithValidRequest_PersistsTransferBalancesAndIdempotency()
    {
        var accounts =
            await SeedAccountsAsync(
                sourceBalance: 1000.00m,
                destinationBalance: 100.00m);

        var idempotencyKey =
            Guid.NewGuid();

        ExecuteTransferResult result;

        await using (
            var executionContext =
                Fixture.CreateDbContext())
        {
            var handler =
                CreateHandler(
                    executionContext);

            result =
                await handler.HandleAsync(
                    new ExecuteTransferCommand(
                        idempotencyKey,
                        accounts.SourceAccountId,
                        accounts.DestinationAccountId,
                        250.00m));
        }

        Assert.False(
            result.IsReplay);

        Assert.Equal(
            TransferStatus.Completed,
            result.Status);

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
            result.Id,
            transfer.Id);

        Assert.Equal(
            250.00m,
            transfer.Amount.Amount);

        Assert.Equal(
            TransferStatus.Completed,
            transfer.Status);

        var idempotencyRecord =
            Assert.Single(
                idempotencyRecords);

        Assert.Equal(
            transfer.Id,
            idempotencyRecord.TransferId);

        Assert.Equal(
            250.00m,
            idempotencyRecord.Amount);

        Assert.NotNull(
            idempotencyRecord.CompletedAt);
    }

    [Fact]
    public async Task ExecuteTransfer_WithInsufficientFunds_RollsBackEverythingIncludingIdempotencyClaim()
    {
        var accounts =
            await SeedAccountsAsync(
                sourceBalance: 50.00m,
                destinationBalance: 100.00m);

        var idempotencyKey =
            Guid.NewGuid();

        await using (
            var executionContext =
                Fixture.CreateDbContext())
        {
            var handler =
                CreateHandler(
                    executionContext);

            await Assert.ThrowsAsync<InsufficientFundsException>(
                () =>
                    handler.HandleAsync(
                        new ExecuteTransferCommand(
                            idempotencyKey,
                            accounts.SourceAccountId,
                            accounts.DestinationAccountId,
                            100.00m)));
        }

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

        var transferCount =
            await verificationContext.Transfers
                .AsNoTracking()
                .CountAsync();

        var idempotencyCount =
            await verificationContext
                .Set<TransferIdempotencyRecord>()
                .AsNoTracking()
                .CountAsync(
                    record =>
                        record.IdempotencyKey
                            == idempotencyKey);

        Assert.Equal(
            50.00m,
            source.Balance.Amount);

        Assert.Equal(
            100.00m,
            destination.Balance.Amount);

        Assert.Equal(
            0,
            transferCount);

        Assert.Equal(
            0,
            idempotencyCount);
    }

    [Fact]
    public async Task ExecuteTransfer_WithSameKeyAndPayload_ReturnsReplayWithoutDuplicatingTransfer()
    {
        var accounts =
            await SeedAccountsAsync(
                sourceBalance: 1000.00m,
                destinationBalance: 100.00m);

        var idempotencyKey =
            Guid.NewGuid();

        ExecuteTransferResult firstResult;

        await using (
            var firstRequestContext =
                Fixture.CreateDbContext())
        {
            var handler =
                CreateHandler(
                    firstRequestContext);

            firstResult =
                await handler.HandleAsync(
                    new ExecuteTransferCommand(
                        idempotencyKey,
                        accounts.SourceAccountId,
                        accounts.DestinationAccountId,
                        250.00m));
        }

        ExecuteTransferResult replayResult;

        await using (
            var replayRequestContext =
                Fixture.CreateDbContext())
        {
            var handler =
                CreateHandler(
                    replayRequestContext);

            replayResult =
                await handler.HandleAsync(
                    new ExecuteTransferCommand(
                        idempotencyKey,
                        accounts.SourceAccountId,
                        accounts.DestinationAccountId,
                        250.00m));
        }

        Assert.False(
            firstResult.IsReplay);

        Assert.True(
            replayResult.IsReplay);

        Assert.Equal(
            firstResult.Id,
            replayResult.Id);

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

        var transferCount =
            await verificationContext.Transfers
                .AsNoTracking()
                .CountAsync(
                    transfer =>
                        transfer.SourceAccountId
                            == accounts.SourceAccountId
                        && transfer.DestinationAccountId
                            == accounts.DestinationAccountId);

        var idempotencyCount =
            await verificationContext
                .Set<TransferIdempotencyRecord>()
                .AsNoTracking()
                .CountAsync(
                    record =>
                        record.IdempotencyKey
                            == idempotencyKey);

        Assert.Equal(
            750.00m,
            source.Balance.Amount);

        Assert.Equal(
            350.00m,
            destination.Balance.Amount);

        Assert.Equal(
            1,
            transferCount);

        Assert.Equal(
            1,
            idempotencyCount);
    }

    [Fact]
    public async Task ExecuteTransfer_WithSameKeyAndDifferentPayload_ThrowsConflictWithoutMovingMoneyAgain()
    {
        var accounts =
            await SeedAccountsAsync(
                sourceBalance: 1000.00m,
                destinationBalance: 100.00m);

        var idempotencyKey =
            Guid.NewGuid();

        ExecuteTransferResult firstResult;

        await using (
            var firstRequestContext =
                Fixture.CreateDbContext())
        {
            var handler =
                CreateHandler(
                    firstRequestContext);

            firstResult =
                await handler.HandleAsync(
                    new ExecuteTransferCommand(
                        idempotencyKey,
                        accounts.SourceAccountId,
                        accounts.DestinationAccountId,
                        250.00m));
        }

        await using (
            var conflictingRequestContext =
                Fixture.CreateDbContext())
        {
            var handler =
                CreateHandler(
                    conflictingRequestContext);

            var exception =
                await Assert.ThrowsAsync<IdempotencyKeyConflictException>(
                    () =>
                        handler.HandleAsync(
                            new ExecuteTransferCommand(
                                idempotencyKey,
                                accounts.SourceAccountId,
                                accounts.DestinationAccountId,
                                251.00m)));

            Assert.Equal(
                idempotencyKey,
                exception.IdempotencyKey);
        }

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

        Assert.Equal(
            750.00m,
            source.Balance.Amount);

        Assert.Equal(
            350.00m,
            destination.Balance.Amount);

        var transfer =
            Assert.Single(
                transfers);

        Assert.Equal(
            firstResult.Id,
            transfer.Id);

        Assert.Equal(
            250.00m,
            transfer.Amount.Amount);

        var idempotencyRecord =
            Assert.Single(
                idempotencyRecords);

        Assert.Equal(
            250.00m,
            idempotencyRecord.Amount);

        Assert.Equal(
            firstResult.Id,
            idempotencyRecord.TransferId);
    }

    private async Task<SeededAccounts> SeedAccountsAsync(
        decimal sourceBalance,
        decimal destinationBalance)
    {
        await using var dbContext =
            Fixture.CreateDbContext();

        var occurredAt =
            DateTimeOffset.UtcNow;

        var source =
            Account.Create(
                "INT-SOURCE-001",
                "Integration Source",
                new Money(
                    sourceBalance),
                occurredAt);

        var destination =
            Account.Create(
                "INT-DESTINATION-001",
                "Integration Destination",
                new Money(
                    destinationBalance),
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
        Guid DestinationAccountId);
}
