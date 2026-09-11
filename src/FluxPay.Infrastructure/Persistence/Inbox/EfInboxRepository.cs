using FluxPay.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.Infrastructure.Persistence.Inbox;

public sealed class EfInboxRepository
    : IInboxRepository
{
    private readonly FluxPayDbContext _dbContext;

    public EfInboxRepository(
        FluxPayDbContext dbContext)
    {
        _dbContext =
            dbContext;
    }

    public async Task<bool> TryClaimAsync(
        string consumerName,
        Guid messageId,
        string eventType,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Inbox claims require an active database transaction.");
        }

        if (string.IsNullOrWhiteSpace(consumerName))
        {
            throw new ArgumentException(
                "Consumer name is required.",
                nameof(consumerName));
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException(
                "Event type is required.",
                nameof(eventType));
        }

        var affectedRows =
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO consumer_inbox_messages
                (
                    consumer_name,
                    message_id,
                    event_type,
                    received_at,
                    processed_at
                )
                VALUES
                (
                    {consumerName},
                    {messageId},
                    {eventType},
                    {receivedAt.ToUniversalTime()},
                    NULL
                )
                ON CONFLICT
                (
                    consumer_name,
                    message_id
                )
                DO NOTHING
                """,
                cancellationToken);

        if (affectedRows == 1)
        {
            return true;
        }

        if (affectedRows != 0)
        {
            throw new InvalidOperationException(
                $"Unexpected affected row count while claiming inbox message '{messageId}' for consumer '{consumerName}': {affectedRows}.");
        }

        var existingMessage =
            await _dbContext.InboxMessages
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    message =>
                        message.ConsumerName
                            == consumerName
                        && message.MessageId
                            == messageId,
                    cancellationToken);

        if (existingMessage is null)
        {
            throw new InvalidOperationException(
                $"Inbox message '{messageId}' for consumer '{consumerName}' was not found after a claim conflict.");
        }

        if (
            !string.Equals(
                existingMessage.EventType,
                eventType,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Inbox message '{messageId}' for consumer '{consumerName}' already exists with event type '{existingMessage.EventType}', but '{eventType}' was received.");
        }

        if (existingMessage.ProcessedAt is null)
        {
            throw new InvalidOperationException(
                $"Inbox message '{messageId}' for consumer '{consumerName}' exists but is incomplete.");
        }

        return false;
    }

    public async Task MarkProcessedAsync(
        string consumerName,
        Guid messageId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Completing an inbox message requires an active database transaction.");
        }

        var affectedRows =
            await _dbContext.InboxMessages
                .Where(
                    message =>
                        message.ConsumerName
                            == consumerName
                        && message.MessageId
                            == messageId
                        && message.ProcessedAt
                            == null)
                .ExecuteUpdateAsync(
                    setters =>
                        setters.SetProperty(
                            message =>
                                message.ProcessedAt,
                            (DateTimeOffset?)
                                processedAt.ToUniversalTime()),
                    cancellationToken);

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Expected to complete one inbox message '{messageId}' for consumer '{consumerName}', but updated {affectedRows}.");
        }
    }
}
