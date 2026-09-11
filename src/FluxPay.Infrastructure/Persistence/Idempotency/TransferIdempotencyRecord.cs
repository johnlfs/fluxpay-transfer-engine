namespace FluxPay.Infrastructure.Persistence.Idempotency;

public sealed class TransferIdempotencyRecord
{
    public Guid IdempotencyKey { get; set; }

    public Guid SourceAccountId { get; set; }

    public Guid DestinationAccountId { get; set; }

    public decimal Amount { get; set; }

    public Guid? TransferId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
