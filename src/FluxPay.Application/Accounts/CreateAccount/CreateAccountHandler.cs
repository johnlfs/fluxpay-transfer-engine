using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Accounts.Exceptions;
using FluxPay.Domain.Accounts;
using FluxPay.Domain.ValueObjects;

namespace FluxPay.Application.Accounts.CreateAccount;

public sealed class CreateAccountHandler
{
    private readonly IAccountRepository _accountRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public CreateAccountHandler(
        IAccountRepository accountRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _accountRepository = accountRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<CreateAccountResult> HandleAsync(
        CreateAccountCommand command,
        CancellationToken cancellationToken = default)
    {
        var account = Account.Create(
            command.AccountNumber,
            command.OwnerName,
            new Money(command.InitialBalance),
            _timeProvider.GetUtcNow());

        var accountNumberAlreadyExists =
            await _accountRepository.ExistsByAccountNumberAsync(
                account.AccountNumber,
                cancellationToken);

        if (accountNumberAlreadyExists)
        {
            throw new AccountNumberAlreadyExistsException(
                account.AccountNumber);
        }

        await _accountRepository.AddAsync(
            account,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(
            cancellationToken);

        return new CreateAccountResult(
            account.Id,
            account.AccountNumber,
            account.OwnerName,
            account.Balance.Amount,
            account.CreatedAt);
    }
}
