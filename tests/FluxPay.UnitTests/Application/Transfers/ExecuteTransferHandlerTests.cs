using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Accounts.Exceptions;
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
    public async Task HandleAsync_WithValidTransfer_MovesMoneyAndCompletesTransfer()
    {
        var sourceAccount =
            CreateAccount(
                "SOURCE-001",
                1000.00m);

        var destinationAccount =
            CreateAccount(
                "DESTINATION-001",
                100.00m);

        var accountRepository =
            new FakeAccountRepository(
                sourceAccount,
                destinationAccount);

        var transferRepository =
            new FakeTransferRepository();

        var unitOfWork =
            new FakeUnitOfWork();

        var handler =
            CreateHandler(
                accountRepository,
                transferRepository,
                unitOfWork);

        var result =
            await handler.HandleAsync(
                new ExecuteTransferCommand(
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
            FixedUtcNow,
            sourceAccount.UpdatedAt);

        Assert.Equal(
            FixedUtcNow,
            destinationAccount.UpdatedAt);

        Assert.Single(
            transferRepository.AddedTransfers);

        var persistedTransfer =
            transferRepository.AddedTransfers.Single();

        Assert.Equal(
            result.Id,
            persistedTransfer.Id);

        Assert.Equal(
            sourceAccount.Id,
            result.SourceAccountId);

        Assert.Equal(
            destinationAccount.Id,
            result.DestinationAccountId);

        Assert.Equal(
            250.00m,
            result.Amount);

        Assert.Equal(
            TransferStatus.Completed,
            result.Status);

        Assert.Equal(
            FixedUtcNow,
            result.CreatedAt);

        Assert.Equal(
            FixedUtcNow,
            result.FinalizedAt);

        Assert.Equal(
            TransferStatus.Completed,
            persistedTransfer.Status);

        Assert.Equal(
            FixedUtcNow,
            persistedTransfer.FinalizedAt);

        Assert.Equal(
            1,
            unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenSourceAccountDoesNotExist_ThrowsAndDoesNotPersist()
    {
        var destinationAccount =
            CreateAccount(
                "DESTINATION-002",
                100.00m);

        var missingSourceAccountId =
            Guid.NewGuid();

        var accountRepository =
            new FakeAccountRepository(
                destinationAccount);

        var transferRepository =
            new FakeTransferRepository();

        var unitOfWork =
            new FakeUnitOfWork();

        var handler =
            CreateHandler(
                accountRepository,
                transferRepository,
                unitOfWork);

        var exception =
            await Assert.ThrowsAsync<AccountNotFoundException>(
                () =>
                    handler.HandleAsync(
                        new ExecuteTransferCommand(
                            missingSourceAccountId,
                            destinationAccount.Id,
                            50.00m)));

        Assert.Equal(
            missingSourceAccountId,
            exception.AccountId);

        Assert.Equal(
            100.00m,
            destinationAccount.Balance.Amount);

        Assert.Empty(
            transferRepository.AddedTransfers);

        Assert.Equal(
            0,
            unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenDestinationAccountDoesNotExist_DoesNotDebitSource()
    {
        var sourceAccount =
            CreateAccount(
                "SOURCE-003",
                1000.00m);

        var missingDestinationAccountId =
            Guid.NewGuid();

        var accountRepository =
            new FakeAccountRepository(
                sourceAccount);

        var transferRepository =
            new FakeTransferRepository();

        var unitOfWork =
            new FakeUnitOfWork();

        var handler =
            CreateHandler(
                accountRepository,
                transferRepository,
                unitOfWork);

        var exception =
            await Assert.ThrowsAsync<AccountNotFoundException>(
                () =>
                    handler.HandleAsync(
                        new ExecuteTransferCommand(
                            sourceAccount.Id,
                            missingDestinationAccountId,
                            250.00m)));

        Assert.Equal(
            missingDestinationAccountId,
            exception.AccountId);

        Assert.Equal(
            1000.00m,
            sourceAccount.Balance.Amount);

        Assert.Empty(
            transferRepository.AddedTransfers);

        Assert.Equal(
            0,
            unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithInsufficientFunds_DoesNotChangeBalancesOrPersist()
    {
        var sourceAccount =
            CreateAccount(
                "SOURCE-004",
                50.00m);

        var destinationAccount =
            CreateAccount(
                "DESTINATION-004",
                100.00m);

        var accountRepository =
            new FakeAccountRepository(
                sourceAccount,
                destinationAccount);

        var transferRepository =
            new FakeTransferRepository();

        var unitOfWork =
            new FakeUnitOfWork();

        var handler =
            CreateHandler(
                accountRepository,
                transferRepository,
                unitOfWork);

        var exception =
            await Assert.ThrowsAsync<InsufficientFundsException>(
                () =>
                    handler.HandleAsync(
                        new ExecuteTransferCommand(
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

        Assert.Empty(
            transferRepository.AddedTransfers);

        Assert.Equal(
            0,
            unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithSameSourceAndDestination_FailsBeforeRepositoryAccess()
    {
        var account =
            CreateAccount(
                "ACCOUNT-005",
                1000.00m);

        var accountRepository =
            new FakeAccountRepository(
                account);

        var transferRepository =
            new FakeTransferRepository();

        var unitOfWork =
            new FakeUnitOfWork();

        var handler =
            CreateHandler(
                accountRepository,
                transferRepository,
                unitOfWork);

        var exception =
            await Assert.ThrowsAsync<DomainValidationException>(
                () =>
                    handler.HandleAsync(
                        new ExecuteTransferCommand(
                            account.Id,
                            account.Id,
                            100.00m)));

        Assert.Equal(
            "Source and destination accounts must be different.",
            exception.Message);

        Assert.Equal(
            0,
            accountRepository.GetByIdCallCount);

        Assert.Equal(
            1000.00m,
            account.Balance.Amount);

        Assert.Empty(
            transferRepository.AddedTransfers);

        Assert.Equal(
            0,
            unitOfWork.SaveChangesCallCount);
    }

    private static ExecuteTransferHandler CreateHandler(
        IAccountRepository accountRepository,
        ITransferRepository transferRepository,
        IUnitOfWork unitOfWork)
    {
        return new ExecuteTransferHandler(
            accountRepository,
            transferRepository,
            unitOfWork,
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

    private sealed class FakeAccountRepository
        : IAccountRepository
    {
        private readonly Dictionary<Guid, Account> _accounts;

        public FakeAccountRepository(
            params Account[] accounts)
        {
            _accounts =
                accounts.ToDictionary(
                    account => account.Id);
        }

        public int GetByIdCallCount { get; private set; }

        public Task<bool> ExistsByAccountNumberAsync(
            string accountNumber,
            CancellationToken cancellationToken = default)
        {
            var exists =
                _accounts.Values.Any(
                    account =>
                        account.AccountNumber == accountNumber);

            return Task.FromResult(
                exists);
        }

        public Task<Account?> GetByIdAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
        {
            GetByIdCallCount++;

            _accounts.TryGetValue(
                accountId,
                out var account);

            return Task.FromResult(
                account);
        }

        public Task AddAsync(
            Account account,
            CancellationToken cancellationToken = default)
        {
            _accounts.Add(
                account.Id,
                account);

            return Task.CompletedTask;
        }
    }

    private sealed class FakeTransferRepository
        : ITransferRepository
    {
        public List<Transfer> AddedTransfers { get; } =
            [];

        public Task AddAsync(
            Transfer transfer,
            CancellationToken cancellationToken = default)
        {
            AddedTransfers.Add(
                transfer);

            return Task.CompletedTask;
        }
    }

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
