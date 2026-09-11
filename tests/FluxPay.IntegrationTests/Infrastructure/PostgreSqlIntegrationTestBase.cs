namespace FluxPay.IntegrationTests.Infrastructure;

[Collection(
    PostgreSqlIntegrationCollection.Name)]
public abstract class PostgreSqlIntegrationTestBase
    : IAsyncLifetime
{
    protected PostgreSqlIntegrationTestBase(
        PostgreSqlIntegrationFixture fixture)
    {
        Fixture =
            fixture;
    }

    protected PostgreSqlIntegrationFixture Fixture { get; }

    public Task InitializeAsync()
    {
        return Fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}
