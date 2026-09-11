using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.Infrastructure.Persistence.Repositories;

public sealed class EfTransferRepository : ITransferRepository
{
    private readonly FluxPayDbContext _dbContext;

    public EfTransferRepository(
        FluxPayDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Transfer?> GetByIdAsync(
        Guid transferId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Transfers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                transfer =>
                    transfer.Id == transferId,
                cancellationToken);
    }

    public async Task AddAsync(
        Transfer transfer,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.Transfers.AddAsync(
            transfer,
            cancellationToken);
    }
}
