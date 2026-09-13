using FluxPay.Application.Abstractions.Messaging;
using FluxPay.Application.Messaging.Inbox;
using FluxPay.Infrastructure;
using FluxPay.Infrastructure.Messaging;
using FluxPay.Infrastructure.Observability;
using FluxPay.Infrastructure.Persistence;
using FluxPay.Infrastructure.Persistence.Outbox;
using FluxPay.Worker;
using FluxPay.Worker.Health;
using FluxPay.Worker.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder =
    WebApplication.CreateBuilder(
        args);

string RequiredConfigurationValue(
    string name)
{
    var value =
        builder.Configuration[
            name];

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Configuration value '{name}' is required.");
    }

    return value;
}

int RequiredIntegerConfigurationValue(
    string name)
{
    var value =
        RequiredConfigurationValue(
            name);

    if (
        !int.TryParse(
            value,
            out var parsed))
    {
        throw new InvalidOperationException(
            $"Configuration value '{name}' must contain a valid integer.");
    }

    return parsed;
}

var otlpEndpoint =
    builder.Configuration[
        "OTEL_EXPORTER_OTLP_ENDPOINT"];

var connectionString =
    PostgresConnectionStringResolver.Resolve(
        key =>
            builder.Configuration[
                key]);

var rabbitMqOptions =
    new RabbitMqOptions
    {
        HostName =
            RequiredConfigurationValue(
                "RABBITMQ_HOST"),

        Port =
            RequiredIntegerConfigurationValue(
                "RABBITMQ_PORT"),

        UserName =
            RequiredConfigurationValue(
                "RABBITMQ_USER"),

        Password =
            RequiredConfigurationValue(
                "RABBITMQ_PASSWORD"),

        VirtualHost =
            RequiredConfigurationValue(
                "RABBITMQ_VHOST"),

        ExchangeName =
            RabbitMqOptions
                .DefaultExchangeName,

        ClientProvidedName =
            "fluxpay-outbox-worker"
    };

rabbitMqOptions.Validate();

var workerOptions =
    new OutboxWorkerOptions
    {
        BatchSize =
            builder.Configuration
                .GetValue<int?>(
                    $"{OutboxWorkerOptions.SectionName}:BatchSize")
            ?? 100,

        PollingIntervalMilliseconds =
            builder.Configuration
                .GetValue<int?>(
                    $"{OutboxWorkerOptions.SectionName}:PollingIntervalMilliseconds")
            ?? 1000,

        Parallelism =
            builder.Configuration
                .GetValue<int?>(
                    $"{OutboxWorkerOptions.SectionName}:Parallelism")
            ?? 4
    };

workerOptions.Validate();

var consumerOptions =
    new TransferCompletedConsumerOptions
    {
        ConsumerName =
            builder.Configuration
                .GetValue<string>(
                    $"{TransferCompletedConsumerOptions.SectionName}:ConsumerName")
            ?? TransferCompletedConsumerOptions
                .DefaultConsumerName,

        QueueName =
            builder.Configuration
                .GetValue<string>(
                    $"{TransferCompletedConsumerOptions.SectionName}:QueueName")
            ?? TransferCompletedConsumerOptions
                .DefaultQueueName,

        RetryExchangeName =
            builder.Configuration
                .GetValue<string>(
                    $"{TransferCompletedConsumerOptions.SectionName}:RetryExchangeName")
            ?? TransferCompletedConsumerOptions
                .DefaultRetryExchangeName,

        DeadLetterExchangeName =
            builder.Configuration
                .GetValue<string>(
                    $"{TransferCompletedConsumerOptions.SectionName}:DeadLetterExchangeName")
            ?? TransferCompletedConsumerOptions
                .DefaultDeadLetterExchangeName,

        DeadLetterQueueName =
            builder.Configuration
                .GetValue<string>(
                    $"{TransferCompletedConsumerOptions.SectionName}:DeadLetterQueueName")
            ?? TransferCompletedConsumerOptions
                .DefaultDeadLetterQueueName,

        DeadLetterRoutingKey =
            builder.Configuration
                .GetValue<string>(
                    $"{TransferCompletedConsumerOptions.SectionName}:DeadLetterRoutingKey")
            ?? TransferCompletedConsumerOptions
                .DefaultDeadLetterRoutingKey,

        ClientProvidedName =
            builder.Configuration
                .GetValue<string>(
                    $"{TransferCompletedConsumerOptions.SectionName}:ClientProvidedName")
            ?? "fluxpay-transfer-completed-consumer",

        PrefetchCount =
            builder.Configuration
                .GetValue<int?>(
                    $"{TransferCompletedConsumerOptions.SectionName}:PrefetchCount")
            ?? 1
    };

consumerOptions.Validate();

builder.Services.AddInfrastructure(
    connectionString);

builder.Services.AddSingleton(
    TimeProvider.System);

builder.Services.AddSingleton(
    rabbitMqOptions);

builder.Services.AddSingleton(
    workerOptions);

builder.Services.AddSingleton(
    consumerOptions);

builder.Services.AddSingleton<
    IIntegrationEventPublisher>(
        _ =>
            new RabbitMqPublisherPool(
                rabbitMqOptions,
                workerOptions.Parallelism));

builder.Services.AddScoped<
    OutboxProcessor>();

builder.Services.AddScoped<
    InboxMessageProcessor>();

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<FluxPayDbContext>(
        name:
            "postgresql",
        tags:
            [
                "ready"
            ])
    .AddCheck<RabbitMqHealthCheck>(
        name:
            "rabbitmq",
        tags:
            [
                "ready"
            ]);

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(
        resource =>
            resource.AddService(
                serviceName:
                    "fluxpay-worker",
                serviceVersion:
                    typeof(WorkerMetrics)
                        .Assembly
                        .GetName()
                        .Version?
                        .ToString()))
    .WithMetrics(
        metrics =>
        {
            metrics
                .AddMeter(
                    WorkerMetrics.MeterName)
                .AddRuntimeInstrumentation();

            if (
                !string.IsNullOrWhiteSpace(
                    otlpEndpoint))
            {
                metrics.AddOtlpExporter();
            }
        })
    .WithTracing(
        tracing =>
        {
            tracing
                .AddSource(
                    MessagingActivitySource.Name);

            if (
                !string.IsNullOrWhiteSpace(
                    otlpEndpoint))
            {
                tracing.AddOtlpExporter();
            }
        });

builder.Services.AddHostedService<
    Worker>();

builder.Services.AddHostedService<
    TransferCompletedRabbitMqConsumer>();

var app =
    builder.Build();

app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate =
            _ =>
                false
    });

app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate =
            registration =>
                registration.Tags.Contains(
                    "ready")
    });

await app.RunAsync();
