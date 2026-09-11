using FluxPay.Infrastructure.Persistence.Inbox;
using FluxPay.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.IntegrationTests.Inbox;

public sealed class InboxMessageIntegrationTests
    : PostgreSqlIntegrationTestBase
{
    private const string ConsumerName =
        "transfer-completed-consumer";

    private static readonly DateTimeOffset ReceivedAt =
        new(
            2026,
            9,
            11,
            6,
            0,
            0,
            TimeSpan.Zero);

    public InboxMessageIntegrationTests(
        PostgreSqlIntegrationFixture fixture)
        : base(
            fixture)
    {
    }

    [Fact]
    public async Task InboxMessage_WithValidState_Persists()
    {
        var messageId =
            Guid.NewGuid();

        await using (
            var dbContext =
                Fixture.CreateDbContext())
        {
            var message =
                CreateInboxMessage(
                    ConsumerName,
                    messageId);

            await dbContext.InboxMessages.AddAsync(
                message);

            await dbContext.SaveChangesAsync();
        }

        await using var verificationContext =
            Fixture.CreateDbContext();

        var persisted =
            await verificationContext.InboxMessages
                .AsNoTracking()
                .SingleAsync();

        Assert.Equal(
            ConsumerName,
            persisted.ConsumerName);

        Assert.Equal(
            messageId,
            persisted.MessageId);

        Assert.Equal(
            "transfer.completed.v1",
            persisted.EventType);

        Assert.Equal(
            ReceivedAt,
            persisted.ReceivedAt);

        Assert.Null(
            persisted.ProcessedAt);
    }

    [Fact]
    public async Task InboxMessage_WithSameConsumerAndMessageId_RejectsDuplicate()
    {
        var messageId =
            Guid.NewGuid();

        await using (
            var firstContext =
                Fixture.CreateDbContext())
        {
            await firstContext.InboxMessages.AddAsync(
                CreateInboxMessage(
                    ConsumerName,
                    messageId));

            await firstContext.SaveChangesAsync();
        }

        await using var duplicateContext =
            Fixture.CreateDbContext();

        await duplicateContext.InboxMessages.AddAsync(
            CreateInboxMessage(
                ConsumerName,
                messageId));

        await Assert.ThrowsAsync<DbUpdateException>(
            async () =>
            {
                await duplicateContext.SaveChangesAsync();
            });

        await using var verificationContext =
            Fixture.CreateDbContext();

        var persistedCount =
            await verificationContext.InboxMessages
                .AsNoTracking()
                .CountAsync();

        Assert.Equal(
            1,
            persistedCount);
    }

    [Fact]
    public async Task InboxMessage_WithSameMessageIdForDifferentConsumers_AllowsBoth()
    {
        var messageId =
            Guid.NewGuid();

        await using var dbContext =
            Fixture.CreateDbContext();

        await dbContext.InboxMessages.AddRangeAsync(
            CreateInboxMessage(
                ConsumerName,
                messageId),
            CreateInboxMessage(
                "audit-consumer",
                messageId));

        await dbContext.SaveChangesAsync();

        var persisted =
            await dbContext.InboxMessages
                .AsNoTracking()
                .Where(
                    message =>
                        message.MessageId == messageId)
                .OrderBy(
                    message =>
                        message.ConsumerName)
                .ToListAsync();

        Assert.Equal(
            2,
            persisted.Count);

        Assert.Contains(
            persisted,
            message =>
                message.ConsumerName
                == ConsumerName);

        Assert.Contains(
            persisted,
            message =>
                message.ConsumerName
                == "audit-consumer");
    }

    [Fact]
    public async Task InboxMessage_WhenTransactionRollsBack_DoesNotReserveMessageId()
    {
        var messageId =
            Guid.NewGuid();

        await using (
            var firstContext =
                Fixture.CreateDbContext())
        {
            await using var transaction =
                await firstContext.Database
                    .BeginTransactionAsync();

            await firstContext.InboxMessages.AddAsync(
                CreateInboxMessage(
                    ConsumerName,
                    messageId));

            await firstContext.SaveChangesAsync();

            await transaction.RollbackAsync();
        }

        await using (
            var verificationContext =
                Fixture.CreateDbContext())
        {
            var persistedAfterRollback =
                await verificationContext.InboxMessages
                    .AsNoTracking()
                    .CountAsync(
                        message =>
                            message.ConsumerName
                                == ConsumerName
                            && message.MessageId
                                == messageId);

            Assert.Equal(
                0,
                persistedAfterRollback);
        }

        await using (
            var retryContext =
                Fixture.CreateDbContext())
        {
            await retryContext.InboxMessages.AddAsync(
                CreateInboxMessage(
                    ConsumerName,
                    messageId));

            await retryContext.SaveChangesAsync();
        }

        await using var finalContext =
            Fixture.CreateDbContext();

        var persistedAfterRetry =
            await finalContext.InboxMessages
                .AsNoTracking()
                .CountAsync(
                    message =>
                        message.ConsumerName
                            == ConsumerName
                        && message.MessageId
                            == messageId);

        Assert.Equal(
            1,
            persistedAfterRetry);
    }

    [Fact]
    public async Task InboxMessage_WhenMarkedProcessed_PersistsProcessedAt()
    {
        var messageId =
            Guid.NewGuid();

        await using (
            var creationContext =
                Fixture.CreateDbContext())
        {
            await creationContext.InboxMessages.AddAsync(
                CreateInboxMessage(
                    ConsumerName,
                    messageId));

            await creationContext.SaveChangesAsync();
        }

        var processedAt =
            ReceivedAt.AddSeconds(
                2);

        await using (
            var processingContext =
                Fixture.CreateDbContext())
        {
            var message =
                await processingContext.InboxMessages
                    .SingleAsync(
                        message =>
                            message.ConsumerName
                                == ConsumerName
                            && message.MessageId
                                == messageId);

            message.ProcessedAt =
                processedAt;

            await processingContext.SaveChangesAsync();
        }

        await using var verificationContext =
            Fixture.CreateDbContext();

        var persisted =
            await verificationContext.InboxMessages
                .AsNoTracking()
                .SingleAsync(
                    message =>
                        message.ConsumerName
                            == ConsumerName
                        && message.MessageId
                            == messageId);

        Assert.Equal(
            processedAt,
            persisted.ProcessedAt);
    }

    [Fact]
    public async Task InboxMessage_WithProcessedAtBeforeReceivedAt_IsRejected()
    {
        var message =
            CreateInboxMessage(
                ConsumerName,
                Guid.NewGuid());

        message.ProcessedAt =
            ReceivedAt.AddSeconds(
                -1);

        await using var dbContext =
            Fixture.CreateDbContext();

        await dbContext.InboxMessages.AddAsync(
            message);

        await Assert.ThrowsAsync<DbUpdateException>(
            async () =>
            {
                await dbContext.SaveChangesAsync();
            });
    }

    private static InboxMessage CreateInboxMessage(
        string consumerName,
        Guid messageId)
    {
        return new InboxMessage
        {
            ConsumerName =
                consumerName,

            MessageId =
                messageId,

            EventType =
                "transfer.completed.v1",

            ReceivedAt =
                ReceivedAt,

            ProcessedAt =
                null
        };
    }
}
