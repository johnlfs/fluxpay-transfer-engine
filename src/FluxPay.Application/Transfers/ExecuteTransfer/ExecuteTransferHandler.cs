using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Accounts.Exceptions;
using FluxPay.Domain.Transfers;
using FluxPay.Domain.ValueObjects;

namespace FluxPay.Application.Transfers.ExecuteTransfer;

public sealed class ExecuteTransferHandler
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITransferRepository _transferRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public ExecuteTransferHandler(
        IAccountRepository accountRepository,
        ITransferRepository transferRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _accountRepository = accountRepository;
        _transferRepository = transferRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<ExecuteTransferResult> HandleAsync(
        ExecuteTransferCommand command,
        CancellationToken cancellationToken = default)
    {
        var occurredAt =
            _timeProvider.GetUtcNow();

        var transfer =
            Transfer.Create(
                command.SourceAccountId,
                command.DestinationAccountId,
                new Money(command.Amount),
                occurredAt);

        var sourceAccount =
            await _accountRepository.GetByIdAsync(
                command.SourceAccountId,
                cancellationToken);

        if (sourceAccount is null)
        {
            throw new AccountNotFoundException(
                command.SourceAccountId);
        }

        var destinationAccount =
            await _accountRepository.GetByIdAsync(
                command.DestinationAccountId,
                cancellationToken);

        if (destinationAccount is null)
        {
            throw new AccountNotFoundException(
                command.DestinationAccountId);
        }

        sourceAccount.Debit(
            transfer.Amount,
            occurredAt);

        destinationAccount.Credit(
            transfer.Amount,
            occurredAt);

        transfer.Complete(
            occurredAt);

        await _transferRepository.AddAsync(
            transfer,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(
            cancellationToken);

        return new ExecuteTransferResult(
            transfer.Id,
            transfer.SourceAccountId,
            transfer.DestinationAccountId,
            transfer.Amount.Amount,
            transfer.Status,
            transfer.CreatedAt,
            transfer.FinalizedAt);
    }
}
