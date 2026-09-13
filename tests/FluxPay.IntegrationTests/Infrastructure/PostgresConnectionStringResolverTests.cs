using FluxPay.Infrastructure.Persistence;
using Npgsql;

namespace FluxPay.IntegrationTests.Infrastructure;

public sealed class PostgresConnectionStringResolverTests
{
    [Fact]
    public void Resolve_WithSpecialPasswordCharacters_BuildsParseableConnectionString()
    {
        const string password =
            "pa;ss'word=with-special-chars";

        var values =
            new Dictionary<string, string?>
            {
                ["POSTGRES_HOST"] =
                    "postgres",

                ["POSTGRES_PORT"] =
                    "5432",

                ["POSTGRES_DB"] =
                    "fluxpay",

                ["POSTGRES_USER"] =
                    "fluxpay",

                ["POSTGRES_PASSWORD"] =
                    password
            };

        var connectionString =
            PostgresConnectionStringResolver.Resolve(
                key =>
                    values.GetValueOrDefault(
                        key));

        var parsed =
            new NpgsqlConnectionStringBuilder(
                connectionString);

        Assert.Equal(
            password,
            parsed.Password);

        Assert.Equal(
            "postgres",
            parsed.Host);

        Assert.Equal(
            5432,
            parsed.Port);
    }

    [Fact]
    public void Resolve_WithExplicitConnectionString_UsesItDirectly()
    {
        const string expected =
            "Host=custom;Database=fluxpay";

        var connectionString =
            PostgresConnectionStringResolver.Resolve(
                key =>
                    key == "FLUXPAY_DB_CONNECTION"
                        ? expected
                        : null);

        Assert.Equal(
            expected,
            connectionString);
    }
}
