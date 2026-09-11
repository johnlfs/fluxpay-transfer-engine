namespace FluxPay.Application.Transfers.Events;

public sealed record TransferCompletedIntegrationEvent(
    Guid EventId,
    Guid TransferId,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    DateTimeOffset OccurredAt)
{
    public const string EventType =
        "transfer.completed.v1";
}
