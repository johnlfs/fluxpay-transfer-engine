using Npgsql;

namespace FluxPay.Infrastructure.Persistence;

public static class PostgresConnectionStringResolver
{
    public static string Resolve(
        Func<string, string?> valueProvider)
    {
        ArgumentNullException.ThrowIfNull(
            valueProvider);

        var explicitConnectionString =
            valueProvider(
                "FLUXPAY_DB_CONNECTION");

        if (
            !string.IsNullOrWhiteSpace(
                explicitConnectionString))
        {
            return explicitConnectionString;
        }

        var portValue =
            Required(
                valueProvider,
                "POSTGRES_PORT");

        if (
            !int.TryParse(
                portValue,
                out var port)
            || port <= 0
            || port > 65535)
        {
            throw new InvalidOperationException(
                "Configuration value POSTGRES_PORT must contain a valid TCP port.");
        }

        var builder =
            new NpgsqlConnectionStringBuilder
            {
                Host =
                    Required(
                        valueProvider,
                        "POSTGRES_HOST"),

                Port =
                    port,

                Database =
                    Required(
                        valueProvider,
                        "POSTGRES_DB"),

                Username =
                    Required(
                        valueProvider,
                        "POSTGRES_USER"),

                Password =
                    Required(
                        valueProvider,
                        "POSTGRES_PASSWORD"),

                IncludeErrorDetail =
                    false
            };

        return builder.ConnectionString;
    }

    private static string Required(
        Func<string, string?> valueProvider,
        string name)
    {
        var value =
            valueProvider(
                name);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Configuration value {name} is required.");
        }

        return value;
    }
}
