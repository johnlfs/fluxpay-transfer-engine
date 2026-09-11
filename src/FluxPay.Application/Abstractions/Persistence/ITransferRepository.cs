using FluxPay.Domain.Transfers;

namespace FluxPay.Application.Abstractions.Persistence;

public interface ITransferRepository
{
    Task<Transfer?> GetByIdAsync(
        Guid transferId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Transfer transfer,
        CancellationToken cancellationToken = default);
}
