using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Accounts.Exceptions;
using FluxPay.Domain.Transfers;
using FluxPay.Domain.ValueObjects;

namespace FluxPay.Application.Transfers.ExecuteTransfer;

public sealed class ExecuteTransferHandler
{
    private readonly ITransferAccountRepository _accountRepository;
    private readonly ITransferRepository _transferRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionManager _transactionManager;
    private readonly TimeProvider _timeProvider;

    public ExecuteTransferHandler(
        ITransferAccountRepository accountRepository,
        ITransferRepository transferRepository,
        IUnitOfWork unitOfWork,
        ITransactionManager transactionManager,
        TimeProvider timeProvider)
    {
        _accountRepository = accountRepository;
        _transferRepository = transferRepository;
        _unitOfWork = unitOfWork;
        _transactionManager = transactionManager;
        _timeProvider = timeProvider;
    }

    public async Task<ExecuteTransferResult> HandleAsync(
        ExecuteTransferCommand command,
        CancellationToken cancellationToken = default)
    {
        var createdAt =
            _timeProvider.GetUtcNow();

        var transfer =
            Transfer.Create(
                command.SourceAccountId,
                command.DestinationAccountId,
                new Money(command.Amount),
                createdAt);

        return await _transactionManager.ExecuteAsync(
            async transactionCancellationToken =>
            {
                var accounts =
                    await _accountRepository.GetForTransferAsync(
                        command.SourceAccountId,
                        command.DestinationAccountId,
                        transactionCancellationToken);

                if (accounts.Source is null)
                {
                    throw new AccountNotFoundException(
                        command.SourceAccountId);
                }

                if (accounts.Destination is null)
                {
                    throw new AccountNotFoundException(
                        command.DestinationAccountId);
                }

                var occurredAt =
                    _timeProvider.GetUtcNow();

                accounts.Source.Debit(
                    transfer.Amount,
                    occurredAt);

                accounts.Destination.Credit(
                    transfer.Amount,
                    occurredAt);

                transfer.Complete(
                    occurredAt);

                await _transferRepository.AddAsync(
                    transfer,
                    transactionCancellationToken);

                await _unitOfWork.SaveChangesAsync(
                    transactionCancellationToken);

                return new ExecuteTransferResult(
                    transfer.Id,
                    transfer.SourceAccountId,
                    transfer.DestinationAccountId,
                    transfer.Amount.Amount,
                    transfer.Status,
                    transfer.CreatedAt,
                    transfer.FinalizedAt);
            },
            cancellationToken);
    }
}
