using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Accounts.Exceptions;
using FluxPay.Application.Common.Time;
using FluxPay.Domain.Accounts;
using FluxPay.Domain.ValueObjects;

namespace FluxPay.Application.Accounts.CreateAccount;

public sealed class CreateAccountHandler
{
    private readonly IAccountRepository _accountRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionManager _transactionManager;
    private readonly TimeProvider _timeProvider;

    public CreateAccountHandler(
        IAccountRepository accountRepository,
        IUnitOfWork unitOfWork,
        ITransactionManager transactionManager,
        TimeProvider timeProvider)
    {
        _accountRepository =
            accountRepository;

        _unitOfWork =
            unitOfWork;

        _transactionManager =
            transactionManager;

        _timeProvider =
            timeProvider;
    }

    public async Task<CreateAccountResult> HandleAsync(
        CreateAccountCommand command,
        CancellationToken cancellationToken = default)
    {
        var account =
            Account.Create(
                command.AccountNumber,
                command.OwnerName,
                new Money(
                    command.InitialBalance),
                UtcTimestamp.GetUtcNow(
                    _timeProvider));

        return await _transactionManager.ExecuteAsync(
            async transactionCancellationToken =>
            {
                var accountNumberAlreadyExists =
                    await _accountRepository
                        .ExistsByAccountNumberAsync(
                            account.AccountNumber,
                            transactionCancellationToken);

                if (accountNumberAlreadyExists)
                {
                    throw new AccountNumberAlreadyExistsException(
                        account.AccountNumber);
                }

                await _accountRepository.AddAsync(
                    account,
                    transactionCancellationToken);

                await _unitOfWork.SaveChangesAsync(
                    transactionCancellationToken);

                return new CreateAccountResult(
                    account.Id,
                    account.AccountNumber,
                    account.OwnerName,
                    account.Balance.Amount,
                    account.CreatedAt);
            },
            cancellationToken);
    }
}
