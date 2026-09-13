using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.Infrastructure.Persistence.Repositories;

public sealed class EfAccountRepository : IAccountRepository
{
    private readonly FluxPayDbContext _dbContext;

    public EfAccountRepository(FluxPayDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsByAccountNumberAsync(
        string accountNumber,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Accounts.AnyAsync(
            account => account.AccountNumber == accountNumber,
            cancellationToken);
    }

    public Task<Account?> GetByIdAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                account => account.Id == accountId,
                cancellationToken);
    }

    public async Task AddAsync(
        Account account,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.Accounts.AddAsync(
            account,
            cancellationToken);
    }
}
