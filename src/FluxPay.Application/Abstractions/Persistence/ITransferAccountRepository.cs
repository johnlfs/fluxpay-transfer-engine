using FluxPay.Domain.Accounts;

namespace FluxPay.Application.Abstractions.Persistence;

public interface ITransferAccountRepository
{
    Task<(Account? Source, Account? Destination)> GetForTransferAsync(
        Guid sourceAccountId,
        Guid destinationAccountId,
        CancellationToken cancellationToken = default);
}
