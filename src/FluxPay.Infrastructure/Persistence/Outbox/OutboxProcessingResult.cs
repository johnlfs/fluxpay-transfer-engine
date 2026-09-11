namespace FluxPay.Infrastructure.Persistence.Outbox;

public sealed record OutboxProcessingResult(
    int Candidates,
    int Published,
    int Failed,
    int Skipped);
