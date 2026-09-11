using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.Infrastructure.Persistence.Repositories;

public sealed class EfTransferAccountRepository
    : ITransferAccountRepository
{
    private readonly FluxPayDbContext _dbContext;

    public EfTransferAccountRepository(
        FluxPayDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(Account? Source, Account? Destination)> GetForTransferAsync(
        Guid sourceAccountId,
        Guid destinationAccountId,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Transfer account locking requires an active database transaction.");
        }

        var accounts =
            await _dbContext.Accounts
                .FromSqlInterpolated(
                    $"""
                    SELECT
                        id,
                        account_number,
                        owner_name,
                        balance,
                        created_at,
                        updated_at
                    FROM accounts
                    WHERE id IN (
                        {sourceAccountId},
                        {destinationAccountId}
                    )
                    ORDER BY id
                    FOR UPDATE
                    """)
                .AsTracking()
                .ToListAsync(
                    cancellationToken);

        var source =
            accounts.SingleOrDefault(
                account =>
                    account.Id == sourceAccountId);

        var destination =
            accounts.SingleOrDefault(
                account =>
                    account.Id == destinationAccountId);

        return (
            Source: source,
            Destination: destination
        );
    }
}
