using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Accounts.Exceptions;

namespace FluxPay.Application.Accounts.GetAccount;

public sealed class GetAccountHandler
{
    private readonly IAccountRepository _accountRepository;

    public GetAccountHandler(
        IAccountRepository accountRepository)
    {
        _accountRepository = accountRepository;
    }

    public async Task<GetAccountResult> HandleAsync(
        GetAccountQuery query,
        CancellationToken cancellationToken = default)
    {
        var account =
            await _accountRepository.GetByIdAsync(
                query.AccountId,
                cancellationToken);

        if (account is null)
        {
            throw new AccountNotFoundException(
                query.AccountId);
        }

        return new GetAccountResult(
            account.Id,
            account.AccountNumber,
            account.OwnerName,
            account.Balance.Amount,
            account.CreatedAt,
            account.UpdatedAt);
    }
}
