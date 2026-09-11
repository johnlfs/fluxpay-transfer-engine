namespace FluxPay.Infrastructure.Persistence.Inbox;

public sealed class InboxMessage
{
    public string ConsumerName { get; set; } =
        string.Empty;

    public Guid MessageId { get; set; }

    public string EventType { get; set; } =
        string.Empty;

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}
