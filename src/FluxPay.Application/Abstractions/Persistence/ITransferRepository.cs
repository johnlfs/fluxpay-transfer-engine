using FluxPay.Domain.Transfers;

namespace FluxPay.Application.Abstractions.Persistence;

public interface ITransferRepository
{
    Task AddAsync(
        Transfer transfer,
        CancellationToken cancellationToken = default);
}
