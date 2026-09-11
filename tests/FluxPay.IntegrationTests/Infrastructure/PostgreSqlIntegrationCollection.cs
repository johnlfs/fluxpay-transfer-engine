namespace FluxPay.IntegrationTests.Infrastructure;

[CollectionDefinition(
    Name,
    DisableParallelization = true)]
public sealed class PostgreSqlIntegrationCollection
    : ICollectionFixture<PostgreSqlIntegrationFixture>
{
    public const string Name =
        "PostgreSQL integration";
}
