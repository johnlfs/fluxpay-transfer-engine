using FluxPay.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FluxPay.IntegrationTests.Persistence;

public sealed class DatabaseBootstrapTests
    : PostgreSqlIntegrationTestBase
{
    public DatabaseBootstrapTests(
        PostgreSqlIntegrationFixture fixture)
        : base(
            fixture)
    {
    }

    [Fact]
    public async Task PostgreSqlContainer_Starts_AndAllMigrationsAreApplied()
    {
        await using var dbContext =
            Fixture.CreateDbContext();

        var canConnect =
            await dbContext.Database.CanConnectAsync();

        var appliedMigrations =
            (
                await dbContext.Database
                    .GetAppliedMigrationsAsync()
            )
            .ToList();

        var pendingMigrations =
            (
                await dbContext.Database
                    .GetPendingMigrationsAsync()
            )
            .ToList();

        Assert.True(
            canConnect);

        Assert.Contains(
            "20260911012543_InitialAccountsSchema",
            appliedMigrations);

        Assert.Contains(
            "20260911020823_AddTransfersSchema",
            appliedMigrations);

        Assert.Contains(
            "20260911024503_AddTransferIdempotency",
            appliedMigrations);

        Assert.Empty(
            pendingMigrations);
    }
}
