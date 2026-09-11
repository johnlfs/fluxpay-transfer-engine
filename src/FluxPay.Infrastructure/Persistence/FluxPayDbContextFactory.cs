using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FluxPay.Infrastructure.Persistence;

public sealed class FluxPayDbContextFactory
    : IDesignTimeDbContextFactory<FluxPayDbContext>
{
    private const string ConnectionStringEnvironmentVariable =
        "FLUXPAY_DB_CONNECTION";

    public FluxPayDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                ConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Environment variable '{ConnectionStringEnvironmentVariable}' is required for Entity Framework design-time operations.");
        }

        var optionsBuilder =
            new DbContextOptionsBuilder<FluxPayDbContext>();

        optionsBuilder.UseNpgsql(connectionString);

        return new FluxPayDbContext(optionsBuilder.Options);
    }
}
