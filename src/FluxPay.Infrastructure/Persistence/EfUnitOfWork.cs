using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Accounts.Exceptions;
using FluxPay.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FluxPay.Infrastructure.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private const string AccountNumberUniqueIndex =
        "ux_accounts_account_number";

    private readonly FluxPayDbContext _dbContext;

    public EfUnitOfWork(FluxPayDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateException exception)
            when (
                exception.InnerException
                    is PostgresException postgresException
                && postgresException.SqlState
                    == PostgresErrorCodes.UniqueViolation
                && postgresException.ConstraintName
                    == AccountNumberUniqueIndex)
        {
            var account =
                exception.Entries
                    .Select(entry => entry.Entity)
                    .OfType<Account>()
                    .FirstOrDefault();

            if (account is null)
            {
                throw;
            }

            throw new AccountNumberAlreadyExistsException(
                account.AccountNumber,
                exception);
        }
    }
}
