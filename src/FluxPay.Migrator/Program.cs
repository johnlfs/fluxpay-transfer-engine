using FluxPay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

string connectionString;

try
{
    connectionString =
        PostgresConnectionStringResolver.Resolve(
            Environment.GetEnvironmentVariable);
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "FluxPay database configuration is invalid.");

    Console.Error.WriteLine(
        $"{exception.GetType().FullName}: {exception.Message}");

    return 1;
}

Console.WriteLine(
    "FluxPay database migrator starting.");

var options =
    new DbContextOptionsBuilder<FluxPayDbContext>()
        .UseNpgsql(
            connectionString)
        .Options;

await using var dbContext =
    new FluxPayDbContext(
        options);

try
{
    var pendingMigrations =
        (
            await dbContext.Database
                .GetPendingMigrationsAsync()
        )
        .ToArray();

    Console.WriteLine(
        $"Pending migrations: {pendingMigrations.Length}");

    foreach (
        var migration
        in pendingMigrations)
    {
        Console.WriteLine(
            $"Pending: {migration}");
    }

    await dbContext.Database
        .MigrateAsync();

    var appliedMigrations =
        (
            await dbContext.Database
                .GetAppliedMigrationsAsync()
        )
        .ToArray();

    Console.WriteLine(
        $"Database migration completed successfully. Applied migrations: {appliedMigrations.Length}");

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "Database migration failed.");

    Console.Error.WriteLine(
        $"{exception.GetType().FullName}: {exception.Message}");

    return 1;
}
