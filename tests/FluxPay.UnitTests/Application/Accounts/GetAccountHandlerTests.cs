using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Accounts.Exceptions;
using FluxPay.Application.Accounts.GetAccount;
using FluxPay.Domain.Accounts;
using FluxPay.Domain.ValueObjects;

namespace FluxPay.UnitTests.Application.Accounts;

public sealed class GetAccountHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenAccountExists_ReturnsAccount()
    {
        var createdAt =
            new DateTimeOffset(
                2026,
                9,
                11,
                2,
                0,
                0,
                TimeSpan.Zero);

        var account =
            Account.Create(
                "ACC-GET-001",
                "Jane Doe",
                new Money(750.25m),
                createdAt);

        var repository =
            new FakeAccountRepository(account);

        var handler =
            new GetAccountHandler(repository);

        var result =
            await handler.HandleAsync(
                new GetAccountQuery(account.Id));

        Assert.Equal(account.Id, result.Id);
        Assert.Equal(
            account.AccountNumber,
            result.AccountNumber);
        Assert.Equal(
            account.OwnerName,
            result.OwnerName);
        Assert.Equal(
            account.Balance.Amount,
            result.Balance);
        Assert.Equal(
            account.CreatedAt,
            result.CreatedAt);
        Assert.Equal(
            account.UpdatedAt,
            result.UpdatedAt);

        Assert.Equal(1, repository.GetByIdCalls);
        Assert.Equal(
            account.Id,
            repository.LastRequestedAccountId);
    }

    [Fact]
    public async Task HandleAsync_WhenAccountDoesNotExist_ThrowsAccountNotFoundException()
    {
        var repository =
            new FakeAccountRepository(account: null);

        var handler =
            new GetAccountHandler(repository);

        var accountId =
            Guid.NewGuid();

        var exception =
            await Assert.ThrowsAsync<AccountNotFoundException>(
                () =>
                    handler.HandleAsync(
                        new GetAccountQuery(accountId)));

        Assert.Equal(
            accountId,
            exception.AccountId);

        Assert.Equal(1, repository.GetByIdCalls);
        Assert.Equal(
            accountId,
            repository.LastRequestedAccountId);
    }

    private sealed class FakeAccountRepository
        : IAccountRepository
    {
        private readonly Account? _account;

        public FakeAccountRepository(
            Account? account)
        {
            _account = account;
        }

        public int GetByIdCalls { get; private set; }

        public Guid? LastRequestedAccountId
        {
            get;
            private set;
        }

        public Task<bool> ExistsByAccountNumberAsync(
            string accountNumber,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<Account?> GetByIdAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
        {
            GetByIdCalls++;
            LastRequestedAccountId = accountId;

            if (_account?.Id == accountId)
            {
                return Task.FromResult<Account?>(
                    _account);
            }

            return Task.FromResult<Account?>(
                null);
        }

        public Task AddAsync(
            Account account,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
