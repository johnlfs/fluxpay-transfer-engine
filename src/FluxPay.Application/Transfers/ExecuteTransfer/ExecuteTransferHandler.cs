using FluxPay.Application.Abstractions.Messaging;
using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Accounts.Exceptions;
using FluxPay.Application.Common.Time;
using FluxPay.Application.Transfers.Events;
using FluxPay.Application.Transfers.Exceptions;
using FluxPay.Domain.Common;
using FluxPay.Domain.Transfers;
using FluxPay.Domain.ValueObjects;

namespace FluxPay.Application.Transfers.ExecuteTransfer;

public sealed class ExecuteTransferHandler
{
    private readonly ITransferAccountRepository _accountRepository;
    private readonly ITransferRepository _transferRepository;
    private readonly ITransferIdempotencyRepository _idempotencyRepository;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionManager _transactionManager;
    private readonly TimeProvider _timeProvider;

    public ExecuteTransferHandler(
        ITransferAccountRepository accountRepository,
        ITransferRepository transferRepository,
        ITransferIdempotencyRepository idempotencyRepository,
        IOutboxWriter outboxWriter,
        IUnitOfWork unitOfWork,
        ITransactionManager transactionManager,
        TimeProvider timeProvider)
    {
        _accountRepository =
            accountRepository;

        _transferRepository =
            transferRepository;

        _idempotencyRepository =
            idempotencyRepository;

        _outboxWriter =
            outboxWriter;

        _unitOfWork =
            unitOfWork;

        _transactionManager =
            transactionManager;

        _timeProvider =
            timeProvider;
    }

    public async Task<ExecuteTransferResult> HandleAsync(
        ExecuteTransferCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.IdempotencyKey == Guid.Empty)
        {
            throw new DomainValidationException(
                "Idempotency key is required.");
        }

        var createdAt =
            UtcTimestamp.GetUtcNow(
                _timeProvider);

        var transfer =
            Transfer.Create(
                command.SourceAccountId,
                command.DestinationAccountId,
                new Money(command.Amount),
                createdAt);

        return await _transactionManager.ExecuteAsync(
            async transactionCancellationToken =>
            {
                var claim =
                    await _idempotencyRepository.TryClaimAsync(
                        command.IdempotencyKey,
                        command.SourceAccountId,
                        command.DestinationAccountId,
                        transfer.Amount.Amount,
                        createdAt,
                        transactionCancellationToken);

                if (
                    claim.Status
                    == TransferIdempotencyClaimStatus.Conflict)
                {
                    throw new IdempotencyKeyConflictException(
                        command.IdempotencyKey);
                }

                if (
                    claim.Status
                    == TransferIdempotencyClaimStatus.Completed)
                {
                    if (claim.TransferId is null)
                    {
                        throw new InvalidOperationException(
                            "Completed idempotency record does not reference a transfer.");
                    }

                    var existingTransfer =
                        await _transferRepository.GetByIdAsync(
                            claim.TransferId.Value,
                            transactionCancellationToken);

                    if (existingTransfer is null)
                    {
                        throw new InvalidOperationException(
                            $"Transfer '{claim.TransferId.Value}' referenced by the idempotency record was not found.");
                    }

                    return ToResult(
                        existingTransfer,
                        isReplay:
                            true);
                }

                if (
                    claim.Status
                    != TransferIdempotencyClaimStatus.Acquired)
                {
                    throw new InvalidOperationException(
                        $"Unsupported idempotency claim status '{claim.Status}'.");
                }

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
                    UtcTimestamp.GetUtcNow(
                        _timeProvider);

                accounts.Source.Debit(
                    transfer.Amount,
                    occurredAt);

                accounts.Destination.Credit(
                    transfer.Amount,
                    occurredAt);

                transfer.Complete(
                    occurredAt);

                var integrationEvent =
                    new TransferCompletedIntegrationEvent(
                        Guid.NewGuid(),
                        transfer.Id,
                        transfer.SourceAccountId,
                        transfer.DestinationAccountId,
                        transfer.Amount.Amount,
                        occurredAt);

                await _transferRepository.AddAsync(
                    transfer,
                    transactionCancellationToken);

                await _outboxWriter.AddAsync(
                    integrationEvent.EventId,
                    TransferCompletedIntegrationEvent.EventType,
                    transfer.Id,
                    integrationEvent,
                    integrationEvent.OccurredAt,
                    transactionCancellationToken);

                await _unitOfWork.SaveChangesAsync(
                    transactionCancellationToken);

                await _idempotencyRepository.CompleteAsync(
                    command.IdempotencyKey,
                    transfer.Id,
                    occurredAt,
                    transactionCancellationToken);

                return ToResult(
                    transfer,
                    isReplay:
                        false);
            },
            cancellationToken);
    }

    private static ExecuteTransferResult ToResult(
        Transfer transfer,
        bool isReplay)
    {
        return new ExecuteTransferResult(
            transfer.Id,
            transfer.SourceAccountId,
            transfer.DestinationAccountId,
            transfer.Amount.Amount,
            transfer.Status,
            transfer.CreatedAt,
            transfer.FinalizedAt,
            isReplay);
    }
}
