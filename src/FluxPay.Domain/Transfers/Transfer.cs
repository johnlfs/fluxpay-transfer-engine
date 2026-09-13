using FluxPay.Domain.Common;
using FluxPay.Domain.ValueObjects;

namespace FluxPay.Domain.Transfers;

public sealed class Transfer
{
    private Transfer(
        Guid id,
        Guid sourceAccountId,
        Guid destinationAccountId,
        Money amount,
        DateTimeOffset createdAt)
    {
        Id = id;
        SourceAccountId = sourceAccountId;
        DestinationAccountId = destinationAccountId;
        Amount = amount;
        Status = TransferStatus.Pending;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid SourceAccountId { get; }

    public Guid DestinationAccountId { get; }

    public Money Amount { get; }

    public TransferStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? FinalizedAt { get; private set; }


    public static Transfer Create(
        Guid sourceAccountId,
        Guid destinationAccountId,
        Money amount,
        DateTimeOffset createdAt)
    {
        if (sourceAccountId == Guid.Empty)
        {
            throw new DomainValidationException(
                "Source account id is required.");
        }

        if (destinationAccountId == Guid.Empty)
        {
            throw new DomainValidationException(
                "Destination account id is required.");
        }

        if (sourceAccountId == destinationAccountId)
        {
            throw new DomainValidationException(
                "Source and destination accounts must be different.");
        }

        if (amount.Amount <= 0)
        {
            throw new DomainValidationException(
                "Transfer amount must be greater than zero.");
        }

        return new Transfer(
            Guid.NewGuid(),
            sourceAccountId,
            destinationAccountId,
            amount,
            createdAt.ToUniversalTime());
    }

    public void Complete(
        DateTimeOffset finalizedAt)
    {
        EnsurePending();

        Status = TransferStatus.Completed;
        FinalizedAt = finalizedAt.ToUniversalTime();
    }

    private void EnsurePending()
    {
        if (Status != TransferStatus.Pending)
        {
            throw new DomainValidationException(
                $"Transfer cannot be changed when its status is '{Status}'.");
        }
    }
}
