using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Application.Transfers.Exceptions;

namespace FluxPay.Application.Transfers.GetTransfer;

public sealed class GetTransferHandler
{
    private readonly ITransferRepository _transferRepository;

    public GetTransferHandler(
        ITransferRepository transferRepository)
    {
        _transferRepository =
            transferRepository;
    }

    public async Task<GetTransferResult> HandleAsync(
        GetTransferQuery query,
        CancellationToken cancellationToken = default)
    {
        var transfer =
            await _transferRepository.GetByIdAsync(
                query.TransferId,
                cancellationToken);

        if (transfer is null)
        {
            throw new TransferNotFoundException(
                query.TransferId);
        }

        return new GetTransferResult(
            transfer.Id,
            transfer.SourceAccountId,
            transfer.DestinationAccountId,
            transfer.Amount.Amount,
            transfer.Status,
            transfer.CreatedAt,
            transfer.FinalizedAt);
    }
}
