using System.Text.Json;
using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Transfers.Events;
using FluxPay.Application.Transfers.Exceptions;
using FluxPay.Application.Transfers.ExecuteTransfer;
using FluxPay.Domain.Accounts;
using FluxPay.Domain.Common;
using FluxPay.Domain.Transfers;
using FluxPay.Domain.ValueObjects;
using FluxPay.Infrastructure.Persistence;
using FluxPay.Infrastructure.Persistence.Idempotency;
using FluxPay.Infrastructure.Persistence.Outbox;
using FluxPay.Infrastructure.Persistence.Repositories;
using FluxPay.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.IntegrationTests.Transfers;

public sealed class TransferExecutionIntegrationTests
    : PostgreSqlIntegrationTestBase
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private const string ForcedFailureMessage =
        "Forced failure after SaveChanges and before transaction commit.";

    public TransferExecutionIntegrationTests(
        PostgreSqlIntegrationFixture fixture)
        : base(
            fixture)
    {
    }

    [Fact]
    public async Task ExecuteTransfer_WithValidRequest_PersistsTransferBalancesIdempotencyAndOutbox()
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

        var outboxMessages =
            await verificationContext.OutboxMessages
                .AsNoTracking()
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

        var outboxMessage =
            Assert.Single(
                outboxMessages);

        Assert.Equal(
            TransferCompletedIntegrationEvent.EventType,
            outboxMessage.EventType);

        Assert.Equal(
            transfer.Id,
            outboxMessage.AggregateId);

        Assert.Null(
            outboxMessage.PublishedAt);

        Assert.Equal(
            0,
            outboxMessage.AttemptCount);

        Assert.Null(
            outboxMessage.LastError);

        var payload =
            JsonSerializer.Deserialize<TransferCompletedIntegrationEvent>(
                outboxMessage.Payload,
                SerializerOptions);

        Assert.NotNull(
            payload);

        Assert.Equal(
            outboxMessage.Id,
            payload.EventId);

        Assert.Equal(
            transfer.Id,
            payload.TransferId);

        Assert.Equal(
            accounts.SourceAccountId,
            payload.SourceAccountId);

        Assert.Equal(
            accounts.DestinationAccountId,
            payload.DestinationAccountId);

        Assert.Equal(
            250.00m,
            payload.Amount);

        var occurredAtDifference =
            (
                outboxMessage.OccurredAt
                - payload.OccurredAt
            )
            .Duration();

        Assert.True(
            occurredAtDifference
            < TimeSpan.FromMicroseconds(1),
            $"Expected the persisted outbox timestamp and payload timestamp "
            + $"to differ by less than one microsecond, but the difference was "
            + $"{occurredAtDifference.TotalMicroseconds} microseconds.");
    }

    [Fact]
    public async Task ExecuteTransfer_WithInsufficientFunds_RollsBackEverythingIncludingIdempotencyAndOutbox()
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

        var outboxCount =
            await verificationContext.OutboxMessages
                .AsNoTracking()
                .CountAsync();

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

        Assert.Equal(
            0,
            outboxCount);
    }

    [Fact]
    public async Task ExecuteTransfer_WithSameKeyAndPayload_ReturnsReplayWithoutDuplicatingTransferOrOutbox()
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

        var outboxMessages =
            await verificationContext.OutboxMessages
                .AsNoTracking()
                .ToListAsync();

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

        var outboxMessage =
            Assert.Single(
                outboxMessages);

        Assert.Equal(
            firstResult.Id,
            outboxMessage.AggregateId);

        Assert.Equal(
            TransferCompletedIntegrationEvent.EventType,
            outboxMessage.EventType);
    }

    [Fact]
    public async Task ExecuteTransfer_WithSameKeyAndDifferentPayload_ThrowsConflictWithoutDuplicatingTransferOrOutbox()
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

        var outboxMessages =
            await verificationContext.OutboxMessages
                .AsNoTracking()
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

        var outboxMessage =
            Assert.Single(
                outboxMessages);

        Assert.Equal(
            firstResult.Id,
            outboxMessage.AggregateId);
    }

    [Fact]
    public async Task ExecuteTransfer_WhenFailureOccursAfterSaveChanges_RollsBackMoneyTransferIdempotencyAndOutbox()
    {
        var accounts =
            await SeedAccountsAsync(
                sourceBalance: 1000.00m,
                destinationBalance: 100.00m);

        var idempotencyKey =
            Guid.NewGuid();

        await using (
            var executionContext =
                Fixture.CreateDbContext())
        {
            var realIdempotencyRepository =
                new EfTransferIdempotencyRepository(
                    executionContext);

            var failingIdempotencyRepository =
                new ThrowOnCompleteTransferIdempotencyRepository(
                    realIdempotencyRepository);

            var handler =
                CreateHandler(
                    executionContext,
                    failingIdempotencyRepository);

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () =>
                        handler.HandleAsync(
                            new ExecuteTransferCommand(
                                idempotencyKey,
                                accounts.SourceAccountId,
                                accounts.DestinationAccountId,
                                250.00m)));

            Assert.Equal(
                ForcedFailureMessage,
                exception.Message);
        }

        await using (
            var rollbackVerificationContext =
                Fixture.CreateDbContext())
        {
            var source =
                await rollbackVerificationContext.Accounts
                    .AsNoTracking()
                    .SingleAsync(
                        account =>
                            account.Id
                            == accounts.SourceAccountId);

            var destination =
                await rollbackVerificationContext.Accounts
                    .AsNoTracking()
                    .SingleAsync(
                        account =>
                            account.Id
                            == accounts.DestinationAccountId);

            var transferCount =
                await rollbackVerificationContext.Transfers
                    .AsNoTracking()
                    .CountAsync();

            var idempotencyCount =
                await rollbackVerificationContext
                    .Set<TransferIdempotencyRecord>()
                    .AsNoTracking()
                    .CountAsync(
                        record =>
                            record.IdempotencyKey
                            == idempotencyKey);

            var outboxCount =
                await rollbackVerificationContext.OutboxMessages
                    .AsNoTracking()
                    .CountAsync();

            Assert.Equal(
                1000.00m,
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

            Assert.Equal(
                0,
                outboxCount);
        }

        ExecuteTransferResult retryResult;

        await using (
            var retryContext =
                Fixture.CreateDbContext())
        {
            var handler =
                CreateHandler(
                    retryContext);

            retryResult =
                await handler.HandleAsync(
                    new ExecuteTransferCommand(
                        idempotencyKey,
                        accounts.SourceAccountId,
                        accounts.DestinationAccountId,
                        250.00m));
        }

        Assert.False(
            retryResult.IsReplay);

        await using var finalVerificationContext =
            Fixture.CreateDbContext();

        var finalSource =
            await finalVerificationContext.Accounts
                .AsNoTracking()
                .SingleAsync(
                    account =>
                        account.Id
                        == accounts.SourceAccountId);

        var finalDestination =
            await finalVerificationContext.Accounts
                .AsNoTracking()
                .SingleAsync(
                    account =>
                        account.Id
                        == accounts.DestinationAccountId);

        var finalTransferCount =
            await finalVerificationContext.Transfers
                .AsNoTracking()
                .CountAsync();

        var finalIdempotencyCount =
            await finalVerificationContext
                .Set<TransferIdempotencyRecord>()
                .AsNoTracking()
                .CountAsync(
                    record =>
                        record.IdempotencyKey
                        == idempotencyKey);

        var finalOutboxMessages =
            await finalVerificationContext.OutboxMessages
                .AsNoTracking()
                .ToListAsync();

        Assert.Equal(
            750.00m,
            finalSource.Balance.Amount);

        Assert.Equal(
            350.00m,
            finalDestination.Balance.Amount);

        Assert.Equal(
            1,
            finalTransferCount);

        Assert.Equal(
            1,
            finalIdempotencyCount);

        var finalOutboxMessage =
            Assert.Single(
                finalOutboxMessages);

        Assert.Equal(
            retryResult.Id,
            finalOutboxMessage.AggregateId);
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
        FluxPayDbContext dbContext,
        ITransferIdempotencyRepository? idempotencyRepository = null)
    {
        var timeProvider =
            TimeProvider.System;

        return new ExecuteTransferHandler(
            new EfTransferAccountRepository(
                dbContext),
            new EfTransferRepository(
                dbContext),
            idempotencyRepository
                ?? new EfTransferIdempotencyRepository(
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

    private sealed class ThrowOnCompleteTransferIdempotencyRepository
        : ITransferIdempotencyRepository
    {
        private readonly ITransferIdempotencyRepository _inner;

        public ThrowOnCompleteTransferIdempotencyRepository(
            ITransferIdempotencyRepository inner)
        {
            _inner =
                inner;
        }

        public Task<TransferIdempotencyClaimResult> TryClaimAsync(
            Guid idempotencyKey,
            Guid sourceAccountId,
            Guid destinationAccountId,
            decimal amount,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken = default)
        {
            return _inner.TryClaimAsync(
                idempotencyKey,
                sourceAccountId,
                destinationAccountId,
                amount,
                createdAt,
                cancellationToken);
        }

        public Task CompleteAsync(
            Guid idempotencyKey,
            Guid transferId,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                ForcedFailureMessage);
        }
    }

    private sealed record SeededAccounts(
        Guid SourceAccountId,
        Guid DestinationAccountId);
}
