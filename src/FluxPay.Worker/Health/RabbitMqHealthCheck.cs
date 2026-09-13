using FluxPay.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace FluxPay.Worker.Health;

public sealed class RabbitMqHealthCheck
    : IHealthCheck
{
    private readonly RabbitMqOptions _options;

    public RabbitMqHealthCheck(
        RabbitMqOptions options)
    {
        _options =
            options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _options.Validate();

            var connectionFactory =
                new ConnectionFactory
                {
                    HostName =
                        _options.HostName,

                    Port =
                        _options.Port,

                    UserName =
                        _options.UserName,

                    Password =
                        _options.Password,

                    VirtualHost =
                        _options.VirtualHost,

                    ClientProvidedName =
                        "fluxpay-worker-health",

                    AutomaticRecoveryEnabled =
                        false,

                    TopologyRecoveryEnabled =
                        false,

                    RequestedHeartbeat =
                        TimeSpan.FromSeconds(
                            30)
                };

            await using var connection =
                await connectionFactory
                    .CreateConnectionAsync(
                        cancellationToken);

            if (!connection.IsOpen)
            {
                return HealthCheckResult.Unhealthy(
                    "RabbitMQ connection is not open.");
            }

            return HealthCheckResult.Healthy(
                "RabbitMQ connection is healthy.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy(
                "RabbitMQ health check was cancelled.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "RabbitMQ connection failed.",
                exception);
        }
    }
}
