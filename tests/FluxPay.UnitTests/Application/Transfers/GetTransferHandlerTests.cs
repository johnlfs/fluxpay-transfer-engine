using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Transfers.Exceptions;
using FluxPay.Application.Transfers.GetTransfer;
using FluxPay.Domain.Transfers;
using FluxPay.Domain.ValueObjects;

namespace FluxPay.UnitTests.Application.Transfers;

public sealed class GetTransferHandlerTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(
            2026,
            9,
            11,
            4,
            0,
            0,
            TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WhenTransferExists_ReturnsTransfer()
    {
        var transfer =
            Transfer.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new Money(
                    250.00m),
                FixedUtcNow);

        transfer.Complete(
            FixedUtcNow.AddSeconds(
                1));

        var repository =
            new FakeTransferRepository(
                transfer);

        var handler =
            new GetTransferHandler(
                repository);

        var result =
            await handler.HandleAsync(
                new GetTransferQuery(
                    transfer.Id));

        Assert.Equal(
            transfer.Id,
            result.Id);

        Assert.Equal(
            transfer.SourceAccountId,
            result.SourceAccountId);

        Assert.Equal(
            transfer.DestinationAccountId,
            result.DestinationAccountId);

        Assert.Equal(
            250.00m,
            result.Amount);

        Assert.Equal(
            TransferStatus.Completed,
            result.Status);

        Assert.Equal(
            FixedUtcNow,
            result.CreatedAt);

        Assert.Equal(
            FixedUtcNow.AddSeconds(
                1),
            result.FinalizedAt);
    }

    [Fact]
    public async Task HandleAsync_WhenTransferDoesNotExist_ThrowsTransferNotFoundException()
    {
        var transferId =
            Guid.NewGuid();

        var handler =
            new GetTransferHandler(
                new FakeTransferRepository());

        var exception =
            await Assert.ThrowsAsync<TransferNotFoundException>(
                () =>
                    handler.HandleAsync(
                        new GetTransferQuery(
                            transferId)));

        Assert.Equal(
            transferId,
            exception.TransferId);
    }

    private sealed class FakeTransferRepository
        : ITransferRepository
    {
        private readonly Dictionary<Guid, Transfer> _transfers;

        public FakeTransferRepository(
            params Transfer[] transfers)
        {
            _transfers =
                transfers.ToDictionary(
                    transfer =>
                        transfer.Id);
        }

        public Task<Transfer?> GetByIdAsync(
            Guid transferId,
            CancellationToken cancellationToken = default)
        {
            _transfers.TryGetValue(
                transferId,
                out var transfer);

            return Task.FromResult(
                transfer);
        }

        public Task AddAsync(
            Transfer transfer,
            CancellationToken cancellationToken = default)
        {
            _transfers[transfer.Id] =
                transfer;

            return Task.CompletedTask;
        }
    }
}
