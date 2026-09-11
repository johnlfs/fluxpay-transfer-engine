using FluxPay.Application.Abstractions.Messaging;
using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Accounts.Exceptions;
using FluxPay.Application.Transfers.Events;
using FluxPay.Application.Transfers.Exceptions;
using FluxPay.Application.Transfers.ExecuteTransfer;
using FluxPay.Domain.Accounts;
using FluxPay.Domain.Common;
using FluxPay.Domain.Transfers;
using FluxPay.Domain.ValueObjects;

namespace FluxPay.UnitTests.Application.Transfers;

public sealed class ExecuteTransferHandlerTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(
            2026,
            9,
            11,
            3,
            30,
            0,
            TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WithValidTransfer_MovesMoneyCompletesIdempotencyAndCreatesOutboxEvent()
    {
        var sourceAccount =
            CreateAccount(
                "SOURCE-001",
                1000.00m);

        var destinationAccount =
            CreateAccount(
                "DESTINATION-001",
                100.00m);

        var idempotencyKey =
            Guid.NewGuid();

        var accountRepository =
            new FakeTransferAccountRepository(
                sourceAccount,
                destinationAccount);

        var transferRepository =
            new FakeTransferRepository();

        var idempotencyRepository =
            new FakeTransferIdempotencyRepository(
                TransferIdempotencyClaimStatus.Acquired);

        var outboxWriter =
            new FakeOutboxWriter();

        var unitOfWork =
            new FakeUnitOfWork();

        var transactionManager =
            new FakeTransactionManager();

        var handler =
            CreateHandler(
                accountRepository,
                transferRepository,
                idempotencyRepository,
                unitOfWork,
                transactionManager,
                outboxWriter);

        var result =
            await handler.HandleAsync(
                new ExecuteTransferCommand(
                    idempotencyKey,
                    sourceAccount.Id,
                    destinationAccount.Id,
                    250.00m));

        Assert.Equal(
            750.00m,
            sourceAccount.Balance.Amount);

        Assert.Equal(
            350.00m,
            destinationAccount.Balance.Amount);

        Assert.Equal(
            TransferStatus.Completed,
            result.Status);

        Assert.False(
            result.IsReplay);

        Assert.Single(
            transferRepository.AddedTransfers);

        Assert.Equal(
            1,
            idempotencyRepository.TryClaimCallCount);

        Assert.Equal(
            1,
            idempotencyRepository.CompleteCallCount);

        Assert.Equal(
            idempotencyKey,
            idempotencyRepository.LastCompletedKey);

        Assert.Equal(
            result.Id,
            idempotencyRepository.LastCompletedTransferId);

        Assert.Equal(
            1,
            accountRepository.GetForTransferCallCount);

        Assert.Equal(
            1,
            unitOfWork.SaveChangesCallCount);

        Assert.Equal(
            1,
            transactionManager.ExecuteCallCount);

        Assert.Equal(
            1,
            outboxWriter.AddCallCount);

        var outboxMessage =
            Assert.Single(
                outboxWriter.AddedMessages);

        Assert.NotEqual(
            Guid.Empty,
            outboxMessage.EventId);

        Assert.Equal(
            TransferCompletedIntegrationEvent.EventType,
            outboxMessage.EventType);

        Assert.Equal(
            result.Id,
            outboxMessage.AggregateId);

        Assert.Equal(
            FixedUtcNow,
            outboxMessage.OccurredAt);

        var payload =
            Assert.IsType<TransferCompletedIntegrationEvent>(
                outboxMessage.Payload);

        Assert.Equal(
            outboxMessage.EventId,
            payload.EventId);

        Assert.Equal(
            result.Id,
            payload.TransferId);

        Assert.Equal(
            sourceAccount.Id,
            payload.SourceAccountId);

        Assert.Equal(
            destinationAccount.Id,
            payload.DestinationAccountId);

        Assert.Equal(
            250.00m,
            payload.Amount);

        Assert.Equal(
            FixedUtcNow,
            payload.OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_WhenRequestWasAlreadyCompleted_ReturnsExistingTransferAsReplayWithoutCreatingOutboxEvent()
    {
        var sourceAccount =
            CreateAccount(
                "SOURCE-REPLAY",
                750.00m);

        var destinationAccount =
            CreateAccount(
                "DESTINATION-REPLAY",
                350.00m);

        var existingTransfer =
            Transfer.Create(
                sourceAccount.Id,
                destinationAccount.Id,
                new Money(250.00m),
                FixedUtcNow.AddMinutes(-1));

        existingTransfer.Complete(
            FixedUtcNow.AddMinutes(-1));

        var accountRepository =
            new FakeTransferAccountRepository(
                sourceAccount,
                destinationAccount);

        var transferRepository =
            new FakeTransferRepository(
                existingTransfer);

        var idempotencyRepository =
            new FakeTransferIdempotencyRepository(
                TransferIdempotencyClaimStatus.Completed,
                existingTransfer.Id);

        var outboxWriter =
            new FakeOutboxWriter();

        var unitOfWork =
            new FakeUnitOfWork();

        var transactionManager =
            new FakeTransactionManager();

        var handler =
            CreateHandler(
                accountRepository,
                transferRepository,
                idempotencyRepository,
                unitOfWork,
                transactionManager,
                outboxWriter);

        var result =
            await handler.HandleAsync(
                new ExecuteTransferCommand(
                    Guid.NewGuid(),
                    sourceAccount.Id,
                    destinationAccount.Id,
                    250.00m));

        Assert.Equal(
            existingTransfer.Id,
            result.Id);

        Assert.True(
            result.IsReplay);

        Assert.Equal(
            750.00m,
            sourceAccount.Balance.Amount);

        Assert.Equal(
            350.00m,
            destinationAccount.Balance.Amount);

        Assert.Equal(
            0,
            accountRepository.GetForTransferCallCount);

        Assert.Equal(
            0,
            transferRepository.AddCallCount);

        Assert.Equal(
            0,
            idempotencyRepository.CompleteCallCount);

        Assert.Equal(
            0,
            outboxWriter.AddCallCount);

        Assert.Equal(
            0,
            unitOfWork.SaveChangesCallCount);

        Assert.Equal(
            1,
            transactionManager.ExecuteCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenIdempotencyKeyHasDifferentPayload_ThrowsConflictBeforeAccountLockAndDoesNotCreateOutboxEvent()
    {
        var sourceAccount =
            CreateAccount(
                "SOURCE-CONFLICT",
                1000.00m);

        var destinationAccount =
            CreateAccount(
                "DESTINATION-CONFLICT",
                100.00m);

        var idempotencyKey =
            Guid.NewGuid();

        var accountRepository =
            new FakeTransferAccountRepository(
                sourceAccount,
                destinationAccount);

        var transferRepository =
            new FakeTransferRepository();

        var idempotencyRepository =
            new FakeTransferIdempotencyRepository(
                TransferIdempotencyClaimStatus.Conflict);

        var outboxWriter =
            new FakeOutboxWriter();

        var unitOfWork =
            new FakeUnitOfWork();

        var transactionManager =
            new FakeTransactionManager();

        var handler =
            CreateHandler(
                accountRepository,
                transferRepository,
                idempotencyRepository,
                unitOfWork,
                transactionManager,
                outboxWriter);

        var exception =
            await Assert.ThrowsAsync<IdempotencyKeyConflictException>(
                () =>
                    handler.HandleAsync(
                        new ExecuteTransferCommand(
                            idempotencyKey,
                            sourceAccount.Id,
                            destinationAccount.Id,
                            250.00m)));

        Assert.Equal(
            idempotencyKey,
            exception.IdempotencyKey);

        Assert.Equal(
            0,
            accountRepository.GetForTransferCallCount);

        Assert.Equal(
            0,
            transferRepository.AddCallCount);

        Assert.Equal(
            0,
            idempotencyRepository.CompleteCallCount);

        Assert.Equal(
            0,
            outboxWriter.AddCallCount);

        Assert.Equal(
            0,
            unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenSourceAccountDoesNotExist_ThrowsAndDoesNotPersistOrCreateOutboxEvent()
    {
        var destinationAccount =
            CreateAccount(
                "DESTINATION-002",
                100.00m);

        var missingSourceAccountId =
            Guid.NewGuid();

        var transferRepository =
            new FakeTransferRepository();

        var idempotencyRepository =
            new FakeTransferIdempotencyRepository(
                TransferIdempotencyClaimStatus.Acquired);

        var outboxWriter =
            new FakeOutboxWriter();

        var unitOfWork =
            new FakeUnitOfWork();

        var handler =
            CreateHandler(
                new FakeTransferAccountRepository(
                    destinationAccount),
                transferRepository,
                idempotencyRepository,
                unitOfWork,
                new FakeTransactionManager(),
                outboxWriter);

        var exception =
            await Assert.ThrowsAsync<AccountNotFoundException>(
                () =>
                    handler.HandleAsync(
                        new ExecuteTransferCommand(
                            Guid.NewGuid(),
                            missingSourceAccountId,
                            destinationAccount.Id,
                            50.00m)));

        Assert.Equal(
            missingSourceAccountId,
            exception.AccountId);

        Assert.Equal(
            0,
            transferRepository.AddCallCount);

        Assert.Equal(
            0,
            idempotencyRepository.CompleteCallCount);

        Assert.Equal(
            0,
            outboxWriter.AddCallCount);

        Assert.Equal(
            0,
            unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenDestinationAccountDoesNotExist_DoesNotDebitSourceOrCreateOutboxEvent()
    {
        var sourceAccount =
            CreateAccount(
                "SOURCE-003",
                1000.00m);

        var missingDestinationAccountId =
            Guid.NewGuid();

        var transferRepository =
            new FakeTransferRepository();

        var idempotencyRepository =
            new FakeTransferIdempotencyRepository(
                TransferIdempotencyClaimStatus.Acquired);

        var outboxWriter =
            new FakeOutboxWriter();

        var unitOfWork =
            new FakeUnitOfWork();

        var handler =
            CreateHandler(
                new FakeTransferAccountRepository(
                    sourceAccount),
                transferRepository,
                idempotencyRepository,
                unitOfWork,
                new FakeTransactionManager(),
                outboxWriter);

        await Assert.ThrowsAsync<AccountNotFoundException>(
            () =>
                handler.HandleAsync(
                    new ExecuteTransferCommand(
                        Guid.NewGuid(),
                        sourceAccount.Id,
                        missingDestinationAccountId,
                        250.00m)));

        Assert.Equal(
            1000.00m,
            sourceAccount.Balance.Amount);

        Assert.Equal(
            0,
            transferRepository.AddCallCount);

        Assert.Equal(
            0,
            idempotencyRepository.CompleteCallCount);

        Assert.Equal(
            0,
            outboxWriter.AddCallCount);

        Assert.Equal(
            0,
            unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithInsufficientFunds_DoesNotPersistTransferCompleteIdempotencyOrCreateOutboxEvent()
    {
        var sourceAccount =
            CreateAccount(
                "SOURCE-004",
                50.00m);

        var destinationAccount =
            CreateAccount(
                "DESTINATION-004",
                100.00m);

        var transferRepository =
            new FakeTransferRepository();

        var idempotencyRepository =
            new FakeTransferIdempotencyRepository(
                TransferIdempotencyClaimStatus.Acquired);

        var outboxWriter =
            new FakeOutboxWriter();

        var unitOfWork =
            new FakeUnitOfWork();

        var handler =
            CreateHandler(
                new FakeTransferAccountRepository(
                    sourceAccount,
                    destinationAccount),
                transferRepository,
                idempotencyRepository,
                unitOfWork,
                new FakeTransactionManager(),
                outboxWriter);

        var exception =
            await Assert.ThrowsAsync<InsufficientFundsException>(
                () =>
                    handler.HandleAsync(
                        new ExecuteTransferCommand(
                            Guid.NewGuid(),
                            sourceAccount.Id,
                            destinationAccount.Id,
                            100.00m)));

        Assert.Equal(
            50.00m,
            exception.AvailableBalance);

        Assert.Equal(
            100.00m,
            exception.RequestedAmount);

        Assert.Equal(
            50.00m,
            sourceAccount.Balance.Amount);

        Assert.Equal(
            100.00m,
            destinationAccount.Balance.Amount);

        Assert.Equal(
            0,
            transferRepository.AddCallCount);

        Assert.Equal(
            0,
            idempotencyRepository.CompleteCallCount);

        Assert.Equal(
            0,
            outboxWriter.AddCallCount);

        Assert.Equal(
            0,
            unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithSameSourceAndDestination_FailsBeforeTransactionAndDoesNotCreateOutboxEvent()
    {
        var account =
            CreateAccount(
                "ACCOUNT-005",
                1000.00m);

        var transactionManager =
            new FakeTransactionManager();

        var outboxWriter =
            new FakeOutboxWriter();

        var handler =
            CreateHandler(
                new FakeTransferAccountRepository(
                    account),
                new FakeTransferRepository(),
                new FakeTransferIdempotencyRepository(
                    TransferIdempotencyClaimStatus.Acquired),
                new FakeUnitOfWork(),
                transactionManager,
                outboxWriter);

        await Assert.ThrowsAsync<DomainValidationException>(
            () =>
                handler.HandleAsync(
                    new ExecuteTransferCommand(
                        Guid.NewGuid(),
                        account.Id,
                        account.Id,
                        100.00m)));

        Assert.Equal(
            0,
            transactionManager.ExecuteCallCount);

        Assert.Equal(
            0,
            outboxWriter.AddCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyIdempotencyKey_FailsBeforeTransactionAndDoesNotCreateOutboxEvent()
    {
        var source =
            CreateAccount(
                "SOURCE-EMPTY-KEY",
                1000.00m);

        var destination =
            CreateAccount(
                "DESTINATION-EMPTY-KEY",
                0.00m);

        var transactionManager =
            new FakeTransactionManager();

        var outboxWriter =
            new FakeOutboxWriter();

        var handler =
            CreateHandler(
                new FakeTransferAccountRepository(
                    source,
                    destination),
                new FakeTransferRepository(),
                new FakeTransferIdempotencyRepository(
                    TransferIdempotencyClaimStatus.Acquired),
                new FakeUnitOfWork(),
                transactionManager,
                outboxWriter);

        var exception =
            await Assert.ThrowsAsync<DomainValidationException>(
                () =>
                    handler.HandleAsync(
                        new ExecuteTransferCommand(
                            Guid.Empty,
                            source.Id,
                            destination.Id,
                            100.00m)));

        Assert.Equal(
            "Idempotency key is required.",
            exception.Message);

        Assert.Equal(
            0,
            transactionManager.ExecuteCallCount);

        Assert.Equal(
            0,
            outboxWriter.AddCallCount);
    }

    private static ExecuteTransferHandler CreateHandler(
        ITransferAccountRepository accountRepository,
        ITransferRepository transferRepository,
        ITransferIdempotencyRepository idempotencyRepository,
        IUnitOfWork unitOfWork,
        ITransactionManager transactionManager,
        IOutboxWriter? outboxWriter = null)
    {
        return new ExecuteTransferHandler(
            accountRepository,
            transferRepository,
            idempotencyRepository,
            outboxWriter
                ?? new FakeOutboxWriter(),
            unitOfWork,
            transactionManager,
            new FixedTimeProvider(
                FixedUtcNow));
    }

    private static Account CreateAccount(
        string accountNumber,
        decimal balance)
    {
        return Account.Create(
            accountNumber,
            $"{accountNumber} Owner",
            new Money(balance),
            FixedUtcNow.AddDays(-1));
    }

    private sealed class FakeTransferAccountRepository
        : ITransferAccountRepository
    {
        private readonly Dictionary<Guid, Account> _accounts;

        public FakeTransferAccountRepository(
            params Account[] accounts)
        {
            _accounts =
                accounts.ToDictionary(
                    account => account.Id);
        }

        public int GetForTransferCallCount { get; private set; }

        public Task<(Account? Source, Account? Destination)> GetForTransferAsync(
            Guid sourceAccountId,
            Guid destinationAccountId,
            CancellationToken cancellationToken = default)
        {
            GetForTransferCallCount++;

            _accounts.TryGetValue(
                sourceAccountId,
                out var source);

            _accounts.TryGetValue(
                destinationAccountId,
                out var destination);

            return Task.FromResult(
                (
                    Source: source,
                    Destination: destination
                ));
        }
    }

    private sealed class FakeTransferRepository
        : ITransferRepository
    {
        private readonly Dictionary<Guid, Transfer> _transfers;

        public FakeTransferRepository(
            params Transfer[] transfers)
        {
            _transfers =
                transfers.ToDictionary(
                    transfer => transfer.Id);
        }

        public int AddCallCount { get; private set; }

        public List<Transfer> AddedTransfers { get; } =
            [];

        public Task<Transfer?> GetByIdAsync(
            Guid transferId,
            CancellationToken cancellationToken = default)
        {
            _transfers.TryGetValue(
                transferId,
                out var transfer);

            return Task.FromResult(
                transfer);
        }

        public Task AddAsync(
            Transfer transfer,
            CancellationToken cancellationToken = default)
        {
            AddCallCount++;

            AddedTransfers.Add(
                transfer);

            _transfers[transfer.Id] =
                transfer;

            return Task.CompletedTask;
        }
    }

    private sealed class FakeTransferIdempotencyRepository
        : ITransferIdempotencyRepository
    {
        private readonly TransferIdempotencyClaimStatus _status;
        private readonly Guid? _transferId;

        public FakeTransferIdempotencyRepository(
            TransferIdempotencyClaimStatus status,
            Guid? transferId = null)
        {
            _status =
                status;

            _transferId =
                transferId;
        }

        public int TryClaimCallCount { get; private set; }

        public int CompleteCallCount { get; private set; }

        public Guid? LastCompletedKey { get; private set; }

        public Guid? LastCompletedTransferId { get; private set; }

        public Task<TransferIdempotencyClaimResult> TryClaimAsync(
            Guid idempotencyKey,
            Guid sourceAccountId,
            Guid destinationAccountId,
            decimal amount,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken = default)
        {
            TryClaimCallCount++;

            return Task.FromResult(
                new TransferIdempotencyClaimResult(
                    _status,
                    _transferId));
        }

        public Task CompleteAsync(
            Guid idempotencyKey,
            Guid transferId,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            CompleteCallCount++;

            LastCompletedKey =
                idempotencyKey;

            LastCompletedTransferId =
                transferId;

            return Task.CompletedTask;
        }
    }

    private sealed class FakeOutboxWriter
        : IOutboxWriter
    {
        public int AddCallCount { get; private set; }

        public List<RecordedOutboxMessage> AddedMessages { get; } =
            [];

        public Task AddAsync<TPayload>(
            Guid eventId,
            string eventType,
            Guid aggregateId,
            TPayload payload,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken = default)
            where TPayload : class
        {
            AddCallCount++;

            AddedMessages.Add(
                new RecordedOutboxMessage(
                    eventId,
                    eventType,
                    aggregateId,
                    payload,
                    occurredAt));

            return Task.CompletedTask;
        }
    }

    private sealed record RecordedOutboxMessage(
        Guid EventId,
        string EventType,
        Guid AggregateId,
        object Payload,
        DateTimeOffset OccurredAt);

    private sealed class FakeUnitOfWork
        : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;

            return Task.FromResult(
                1);
        }
    }

    private sealed class FakeTransactionManager
        : ITransactionManager
    {
        public int ExecuteCallCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            ExecuteCallCount++;

            return await operation(
                cancellationToken);
        }
    }

    private sealed class FixedTimeProvider
        : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow =
                utcNow.ToUniversalTime();
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
