using FluxPay.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.Infrastructure.Persistence;

public sealed class EfTransactionManager : ITransactionManager
{
    private readonly FluxPayDbContext _dbContext;

    public EfTransactionManager(
        FluxPayDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "A database transaction is already active.");
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var result =
                await operation(
                    cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return result;
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
    }
}
