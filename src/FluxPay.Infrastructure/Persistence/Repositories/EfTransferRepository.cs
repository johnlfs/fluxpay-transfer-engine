using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Domain.Transfers;

namespace FluxPay.Infrastructure.Persistence.Repositories;

public sealed class EfTransferRepository : ITransferRepository
{
    private readonly FluxPayDbContext _dbContext;

    public EfTransferRepository(
        FluxPayDbContext dbContext)
    {
        _dbContext = dbContext;
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
