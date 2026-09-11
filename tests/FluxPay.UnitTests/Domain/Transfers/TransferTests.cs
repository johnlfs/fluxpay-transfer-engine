using FluxPay.Domain.Common;
using FluxPay.Domain.Transfers;
using FluxPay.Domain.ValueObjects;

namespace FluxPay.UnitTests.Domain.Transfers;

public sealed class TransferTests
{
    [Fact]
    public void Create_WithValidData_CreatesPendingTransfer()
    {
        var sourceAccountId = Guid.NewGuid();
        var destinationAccountId = Guid.NewGuid();

        var createdAt =
            new DateTimeOffset(
                2026,
                9,
                11,
                3,
                0,
                0,
                TimeSpan.Zero);

        var transfer =
            Transfer.Create(
                sourceAccountId,
                destinationAccountId,
                new Money(250.75m),
                createdAt);

        Assert.NotEqual(Guid.Empty, transfer.Id);
        Assert.Equal(
            sourceAccountId,
            transfer.SourceAccountId);
        Assert.Equal(
            destinationAccountId,
            transfer.DestinationAccountId);
        Assert.Equal(
            250.75m,
            transfer.Amount.Amount);
        Assert.Equal(
            TransferStatus.Pending,
            transfer.Status);
        Assert.Equal(
            createdAt,
            transfer.CreatedAt);
        Assert.Null(transfer.FinalizedAt);
        Assert.Null(transfer.RejectionReason);
    }

    [Fact]
    public void Create_WithEmptySourceAccountId_ThrowsDomainValidationException()
    {
        var exception =
            Assert.Throws<DomainValidationException>(
                () =>
                    Transfer.Create(
                        Guid.Empty,
                        Guid.NewGuid(),
                        new Money(100.00m),
                        DateTimeOffset.UtcNow));

        Assert.Equal(
            "Source account id is required.",
            exception.Message);
    }

    [Fact]
    public void Create_WithEmptyDestinationAccountId_ThrowsDomainValidationException()
    {
        var exception =
            Assert.Throws<DomainValidationException>(
                () =>
                    Transfer.Create(
                        Guid.NewGuid(),
                        Guid.Empty,
                        new Money(100.00m),
                        DateTimeOffset.UtcNow));

        Assert.Equal(
            "Destination account id is required.",
            exception.Message);
    }

    [Fact]
    public void Create_WithSameSourceAndDestination_ThrowsDomainValidationException()
    {
        var accountId =
            Guid.NewGuid();

        var exception =
            Assert.Throws<DomainValidationException>(
                () =>
                    Transfer.Create(
                        accountId,
                        accountId,
                        new Money(100.00m),
                        DateTimeOffset.UtcNow));

        Assert.Equal(
            "Source and destination accounts must be different.",
            exception.Message);
    }

    [Fact]
    public void Create_WithZeroAmount_ThrowsDomainValidationException()
    {
        var exception =
            Assert.Throws<DomainValidationException>(
                () =>
                    Transfer.Create(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        Money.Zero,
                        DateTimeOffset.UtcNow));

        Assert.Equal(
            "Transfer amount must be greater than zero.",
            exception.Message);
    }

    [Fact]
    public void Complete_WhenPending_MarksTransferAsCompleted()
    {
        var transfer =
            CreatePendingTransfer();

        var finalizedAt =
            new DateTimeOffset(
                2026,
                9,
                11,
                3,
                5,
                0,
                TimeSpan.Zero);

        transfer.Complete(finalizedAt);

        Assert.Equal(
            TransferStatus.Completed,
            transfer.Status);
        Assert.Equal(
            finalizedAt,
            transfer.FinalizedAt);
        Assert.Null(
            transfer.RejectionReason);
    }

    [Fact]
    public void Complete_WhenAlreadyCompleted_ThrowsDomainValidationException()
    {
        var transfer =
            CreatePendingTransfer();

        transfer.Complete(
            DateTimeOffset.UtcNow);

        var exception =
            Assert.Throws<DomainValidationException>(
                () =>
                    transfer.Complete(
                        DateTimeOffset.UtcNow));

        Assert.Equal(
            "Transfer cannot be changed when its status is 'Completed'.",
            exception.Message);
    }

    [Fact]
    public void Reject_WhenPending_MarksTransferAsRejected()
    {
        var transfer =
            CreatePendingTransfer();

        var finalizedAt =
            new DateTimeOffset(
                2026,
                9,
                11,
                3,
                10,
                0,
                TimeSpan.Zero);

        transfer.Reject(
            "  Insufficient funds  ",
            finalizedAt);

        Assert.Equal(
            TransferStatus.Rejected,
            transfer.Status);
        Assert.Equal(
            finalizedAt,
            transfer.FinalizedAt);
        Assert.Equal(
            "Insufficient funds",
            transfer.RejectionReason);
    }

    [Fact]
    public void Reject_WithBlankReason_ThrowsAndPreservesPendingState()
    {
        var transfer =
            CreatePendingTransfer();

        var exception =
            Assert.Throws<DomainValidationException>(
                () =>
                    transfer.Reject(
                        "   ",
                        DateTimeOffset.UtcNow));

        Assert.Equal(
            "Transfer rejection reason is required.",
            exception.Message);

        Assert.Equal(
            TransferStatus.Pending,
            transfer.Status);
        Assert.Null(
            transfer.FinalizedAt);
        Assert.Null(
            transfer.RejectionReason);
    }

    [Fact]
    public void Reject_WhenAlreadyCompleted_ThrowsDomainValidationException()
    {
        var transfer =
            CreatePendingTransfer();

        transfer.Complete(
            DateTimeOffset.UtcNow);

        var exception =
            Assert.Throws<DomainValidationException>(
                () =>
                    transfer.Reject(
                        "Should not be possible",
                        DateTimeOffset.UtcNow));

        Assert.Equal(
            "Transfer cannot be changed when its status is 'Completed'.",
            exception.Message);

        Assert.Equal(
            TransferStatus.Completed,
            transfer.Status);
    }

    private static Transfer CreatePendingTransfer()
    {
        return Transfer.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(100.00m),
            DateTimeOffset.UtcNow);
    }
}
