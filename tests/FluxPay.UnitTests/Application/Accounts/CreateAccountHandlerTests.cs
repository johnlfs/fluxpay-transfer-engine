using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Accounts.CreateAccount;
using FluxPay.Application.Accounts.Exceptions;
using FluxPay.Domain.Accounts;
using FluxPay.Domain.Common;

namespace FluxPay.UnitTests.Application.Accounts;

public sealed class CreateAccountHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithValidCommand_CreatesAndPersistsAccount()
    {
        var repository = new FakeAccountRepository();
        var unitOfWork = new FakeUnitOfWork();

        var fixedTime =
            new DateTimeOffset(
                2026,
                9,
                11,
                1,
                30,
                0,
                TimeSpan.Zero);

        var handler = new CreateAccountHandler(
            repository,
            unitOfWork,
            new FakeTransactionManager(),
            new FixedTimeProvider(fixedTime));

        var command = new CreateAccountCommand(
            "ACC-001",
            "John Doe",
            1000.00m);

        var result = await handler.HandleAsync(command);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("ACC-001", result.AccountNumber);
        Assert.Equal("John Doe", result.OwnerName);
        Assert.Equal(1000.00m, result.Balance);
        Assert.Equal(fixedTime, result.CreatedAt);

        Assert.NotNull(repository.AddedAccount);
        Assert.Equal(result.Id, repository.AddedAccount.Id);
        Assert.Equal(1, repository.AddCalls);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task HandleAsync_NormalizesAccountBeforeCheckingForDuplicate()
    {
        var repository = new FakeAccountRepository
        {
            ExistingAccountNumber = "ACC-001"
        };

        var unitOfWork = new FakeUnitOfWork();

        var handler = new CreateAccountHandler(
            repository,
            unitOfWork,
            new FakeTransactionManager(),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        var command = new CreateAccountCommand(
            "  ACC-001  ",
            "John Doe",
            100.00m);

        var exception =
            await Assert.ThrowsAsync<AccountNumberAlreadyExistsException>(
                () => handler.HandleAsync(command));

        Assert.Equal("ACC-001", repository.LastCheckedAccountNumber);
        Assert.Equal("ACC-001", exception.AccountNumber);
        Assert.Equal(0, repository.AddCalls);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task HandleAsync_WhenAccountNumberAlreadyExists_DoesNotPersist()
    {
        var repository = new FakeAccountRepository
        {
            ExistingAccountNumber = "ACC-001"
        };

        var unitOfWork = new FakeUnitOfWork();

        var handler = new CreateAccountHandler(
            repository,
            unitOfWork,
            new FakeTransactionManager(),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        var command = new CreateAccountCommand(
            "ACC-001",
            "Another Owner",
            500.00m);

        await Assert.ThrowsAsync<AccountNumberAlreadyExistsException>(
            () => handler.HandleAsync(command));

        Assert.Equal(0, repository.AddCalls);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidInitialBalance_DoesNotAccessPersistence()
    {
        var repository = new FakeAccountRepository();
        var unitOfWork = new FakeUnitOfWork();

        var handler = new CreateAccountHandler(
            repository,
            unitOfWork,
            new FakeTransactionManager(),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        var command = new CreateAccountCommand(
            "ACC-001",
            "John Doe",
            -1.00m);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => handler.HandleAsync(command));

        Assert.Equal(0, repository.ExistsCalls);
        Assert.Equal(0, repository.AddCalls);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    private sealed class FakeAccountRepository : IAccountRepository
    {
        public string? ExistingAccountNumber { get; init; }

        public string? LastCheckedAccountNumber { get; private set; }

        public Account? AddedAccount { get; private set; }

        public int ExistsCalls { get; private set; }

        public int AddCalls { get; private set; }

        public Task<bool> ExistsByAccountNumberAsync(
            string accountNumber,
            CancellationToken cancellationToken = default)
        {
            ExistsCalls++;
            LastCheckedAccountNumber = accountNumber;

            var exists =
                string.Equals(
                    ExistingAccountNumber,
                    accountNumber,
                    StringComparison.Ordinal);

            return Task.FromResult(exists);
        }

        public Task<Account?> GetByIdAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Account?>(null);
        }

        public Task AddAsync(
            Account account,
            CancellationToken cancellationToken = default)
        {
            AddCalls++;
            AddedAccount = account;

            return Task.CompletedTask;
        }
    }

    private sealed class FakeTransactionManager
        : ITransactionManager
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            return operation(
                cancellationToken);
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCalls { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;

            return Task.FromResult(1);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow.ToUniversalTime();
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
