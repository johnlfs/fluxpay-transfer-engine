using FluxPay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace FluxPay.IntegrationTests.Infrastructure;

public sealed class PostgreSqlIntegrationFixture
    : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;

    public PostgreSqlIntegrationFixture()
    {
        _postgresContainer =
            new PostgreSqlBuilder(
                "postgres:18.6-alpine3.23")
                .WithDatabase(
                    "fluxpay_integration")
                .WithUsername(
                    "fluxpay_integration")
                .WithPassword(
                    "fluxpay_integration_password")
                .Build();
    }

    public string ConnectionString =>
        _postgresContainer.GetConnectionString();

    public FluxPayDbContext CreateDbContext()
    {
        var options =
            new DbContextOptionsBuilder<FluxPayDbContext>()
                .UseNpgsql(
                    ConnectionString)
                .Options;

        return new FluxPayDbContext(
            options);
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();

        await using var dbContext =
            CreateDbContext();

        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        await using var dbContext =
            CreateDbContext();

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM consumer_inbox_messages;
            DELETE FROM outbox_messages;
            DELETE FROM transfer_idempotency;
            DELETE FROM transfers;
            DELETE FROM accounts;
            """);
    }
}
