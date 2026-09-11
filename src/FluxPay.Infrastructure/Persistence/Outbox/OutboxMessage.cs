namespace FluxPay.Infrastructure.Persistence.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    public string EventType { get; set; } =
        string.Empty;

    public Guid AggregateId { get; set; }

    public string Payload { get; set; } =
        string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public DateTimeOffset? DeadLetteredAt { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }
}
