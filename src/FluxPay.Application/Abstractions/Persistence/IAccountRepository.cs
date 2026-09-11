using FluxPay.Domain.Accounts;

namespace FluxPay.Application.Abstractions.Persistence;

public interface IAccountRepository
{
    Task<bool> ExistsByAccountNumberAsync(
        string accountNumber,
        CancellationToken cancellationToken = default);

    Task<Account?> GetByIdAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Account account,
        CancellationToken cancellationToken = default);
}
