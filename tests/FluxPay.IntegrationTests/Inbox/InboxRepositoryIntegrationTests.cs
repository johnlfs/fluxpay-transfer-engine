using FluxPay.Infrastructure.Persistence;
using FluxPay.Infrastructure.Persistence.Inbox;
using FluxPay.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.IntegrationTests.Inbox;

public sealed class InboxRepositoryIntegrationTests
    : PostgreSqlIntegrationTestBase
{
    private const string ConsumerName =
        "transfer-completed-consumer";

    private const string EventType =
        "transfer.completed.v1";

    private static readonly DateTimeOffset ReceivedAt =
        new(
            2026,
            9,
            11,
            6,
            30,
            0,
            TimeSpan.Zero);

    private static readonly DateTimeOffset ProcessedAt =
        ReceivedAt.AddSeconds(
            1);

    public InboxRepositoryIntegrationTests(
        PostgreSqlIntegrationFixture fixture)
        : base(
            fixture)
    {
    }

    [Fact]
    public async Task TryClaimAsync_WithoutTransaction_Throws()
    {
        await using var dbContext =
            Fixture.CreateDbContext();

        var repository =
            new EfInboxRepository(
                dbContext);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await repository.TryClaimAsync(
                        ConsumerName,
                        Guid.NewGuid(),
                        EventType,
                        ReceivedAt);
                });

        Assert.Contains(
            "active database transaction",
            exception.Message);
    }

    [Fact]
    public async Task TryClaimAndMarkProcessed_WhenCommitted_PersistsProcessedMessage()
    {
        var messageId =
            Guid.NewGuid();

        await using (
            var dbContext =
                Fixture.CreateDbContext())
        {
            var repository =
                new EfInboxRepository(
                    dbContext);

            var transactionManager =
                new EfTransactionManager(
                    dbContext);

            var claimed =
                await transactionManager.ExecuteAsync(
                    async cancellationToken =>
                    {
                        var acquired =
                            await repository.TryClaimAsync(
                                ConsumerName,
                                messageId,
                                EventType,
                                ReceivedAt,
                                cancellationToken);

                        Assert.True(
                            acquired);

                        await repository.MarkProcessedAsync(
                            ConsumerName,
                            messageId,
                            ProcessedAt,
                            cancellationToken);

                        return acquired;
                    });

            Assert.True(
                claimed);
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
            EventType,
            persisted.EventType);

        Assert.Equal(
            ReceivedAt,
            persisted.ReceivedAt);

        Assert.Equal(
            ProcessedAt,
            persisted.ProcessedAt);
    }

    [Fact]
    public async Task TryClaimAsync_WhenMessageWasAlreadyProcessed_ReturnsFalse()
    {
        var messageId =
            Guid.NewGuid();

        await ProcessMessageAsync(
            messageId);

        await using var duplicateContext =
            Fixture.CreateDbContext();

        var duplicateRepository =
            new EfInboxRepository(
                duplicateContext);

        var duplicateTransactionManager =
            new EfTransactionManager(
                duplicateContext);

        var claimed =
            await duplicateTransactionManager.ExecuteAsync(
                async cancellationToken =>
                {
                    return await duplicateRepository.TryClaimAsync(
                        ConsumerName,
                        messageId,
                        EventType,
                        ReceivedAt.AddSeconds(
                            10),
                        cancellationToken);
                });

        Assert.False(
            claimed);

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
            ReceivedAt,
            persisted.ReceivedAt);

        Assert.Equal(
            ProcessedAt,
            persisted.ProcessedAt);
    }

    [Fact]
    public async Task TryClaimAsync_WhenTransactionRollsBack_AllowsLaterRetry()
    {
        var messageId =
            Guid.NewGuid();

        await using (
            var failingContext =
                Fixture.CreateDbContext())
        {
            var repository =
                new EfInboxRepository(
                    failingContext);

            var transactionManager =
                new EfTransactionManager(
                    failingContext);

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () =>
                    {
                        await transactionManager.ExecuteAsync<bool>(
                            async cancellationToken =>
                            {
                                var acquired =
                                    await repository.TryClaimAsync(
                                        ConsumerName,
                                        messageId,
                                        EventType,
                                        ReceivedAt,
                                        cancellationToken);

                                Assert.True(
                                    acquired);

                                throw new InvalidOperationException(
                                    "Simulated consumer failure.");
                            });
                    });

            Assert.Equal(
                "Simulated consumer failure.",
                exception.Message);
        }

        await using (
            var verificationContext =
                Fixture.CreateDbContext())
        {
            var count =
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
                count);
        }

        await using var retryContext =
            Fixture.CreateDbContext();

        var retryRepository =
            new EfInboxRepository(
                retryContext);

        var retryTransactionManager =
            new EfTransactionManager(
                retryContext);

        var claimedOnRetry =
            await retryTransactionManager.ExecuteAsync(
                async cancellationToken =>
                {
                    var acquired =
                        await retryRepository.TryClaimAsync(
                            ConsumerName,
                            messageId,
                            EventType,
                            ReceivedAt.AddSeconds(
                                5),
                            cancellationToken);

                    Assert.True(
                        acquired);

                    await retryRepository.MarkProcessedAsync(
                        ConsumerName,
                        messageId,
                        ProcessedAt.AddSeconds(
                            5),
                        cancellationToken);

                    return acquired;
                });

        Assert.True(
            claimedOnRetry);
    }

    [Fact]
    public async Task TryClaimAsync_WithSameMessageIdButDifferentEventType_Throws()
    {
        var messageId =
            Guid.NewGuid();

        await ProcessMessageAsync(
            messageId);

        await using var duplicateContext =
            Fixture.CreateDbContext();

        var repository =
            new EfInboxRepository(
                duplicateContext);

        var transactionManager =
            new EfTransactionManager(
                duplicateContext);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await transactionManager.ExecuteAsync(
                        async cancellationToken =>
                        {
                            return await repository.TryClaimAsync(
                                ConsumerName,
                                messageId,
                                "account.updated.v1",
                                ReceivedAt.AddSeconds(
                                    10),
                                cancellationToken);
                        });
                });

        Assert.Contains(
            "already exists with event type",
            exception.Message);
    }

    [Fact]
    public async Task MarkProcessedAsync_WithoutTransaction_Throws()
    {
        await using var dbContext =
            Fixture.CreateDbContext();

        var repository =
            new EfInboxRepository(
                dbContext);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await repository.MarkProcessedAsync(
                        ConsumerName,
                        Guid.NewGuid(),
                        ProcessedAt);
                });

        Assert.Contains(
            "active database transaction",
            exception.Message);
    }

    [Fact]
    public async Task MarkProcessedAsync_WhenClaimDoesNotExist_Throws()
    {
        await using var dbContext =
            Fixture.CreateDbContext();

        var repository =
            new EfInboxRepository(
                dbContext);

        var transactionManager =
            new EfTransactionManager(
                dbContext);

        var messageId =
            Guid.NewGuid();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await transactionManager.ExecuteAsync(
                        async cancellationToken =>
                        {
                            await repository.MarkProcessedAsync(
                                ConsumerName,
                                messageId,
                                ProcessedAt,
                                cancellationToken);

                            return true;
                        });
                });

        Assert.Contains(
            "updated 0",
            exception.Message);
    }

    private async Task ProcessMessageAsync(
        Guid messageId)
    {
        await using var dbContext =
            Fixture.CreateDbContext();

        var repository =
            new EfInboxRepository(
                dbContext);

        var transactionManager =
            new EfTransactionManager(
                dbContext);

        await transactionManager.ExecuteAsync(
            async cancellationToken =>
            {
                var acquired =
                    await repository.TryClaimAsync(
                        ConsumerName,
                        messageId,
                        EventType,
                        ReceivedAt,
                        cancellationToken);

                Assert.True(
                    acquired);

                await repository.MarkProcessedAsync(
                    ConsumerName,
                    messageId,
                    ProcessedAt,
                    cancellationToken);

                return true;
            });
    }
}
