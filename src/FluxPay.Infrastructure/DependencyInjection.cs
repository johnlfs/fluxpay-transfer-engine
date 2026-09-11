using FluxPay.Application.Abstractions.Messaging;
using FluxPay.Application.Abstractions.Persistence;
using FluxPay.Infrastructure.Persistence;
using FluxPay.Infrastructure.Persistence.Outbox;
using FluxPay.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluxPay.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "Database connection string is required.",
                nameof(connectionString));
        }

        services.AddDbContext<FluxPayDbContext>(
            options =>
            {
                options.UseNpgsql(
                    connectionString);
            });

        services.AddScoped<
            IAccountRepository,
            EfAccountRepository>();

        services.AddScoped<
            ITransferAccountRepository,
            EfTransferAccountRepository>();

        services.AddScoped<
            ITransferRepository,
            EfTransferRepository>();

        services.AddScoped<
            ITransferIdempotencyRepository,
            EfTransferIdempotencyRepository>();

        services.AddScoped<
            IOutboxWriter,
            EfOutboxWriter>();

        services.AddScoped<
            IUnitOfWork,
            EfUnitOfWork>();

        services.AddScoped<
            ITransactionManager,
            EfTransactionManager>();

        return services;
    }
}
