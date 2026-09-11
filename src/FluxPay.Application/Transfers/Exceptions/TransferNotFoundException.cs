namespace FluxPay.Application.Transfers.Exceptions;

public sealed class TransferNotFoundException : Exception
{
    public TransferNotFoundException(
        Guid transferId)
        : base(
            $"Transfer '{transferId}' was not found.")
    {
        TransferId =
            transferId;
    }

    public Guid TransferId { get; }
}
