using FluxPay.Application.Abstractions.Messaging;
using FluxPay.Application.Messaging.Inbox;
using FluxPay.Infrastructure;
using FluxPay.Infrastructure.Messaging;
using FluxPay.Infrastructure.Observability;
using FluxPay.Infrastructure.Persistence.Outbox;
using FluxPay.Worker;
using FluxPay.Worker.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

static string RequiredEnvironmentVariable(
    string name)
{
    var value =
        Environment.GetEnvironmentVariable(
            name);

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Environment variable '{name}' is required.");
    }

    return value;
}

static int RequiredIntegerEnvironmentVariable(
    string name)
{
    var value =
        RequiredEnvironmentVariable(
            name);

    if (
        !int.TryParse(
            value,
            out var parsed))
    {
        throw new InvalidOperationException(
            $"Environment variable '{name}' must contain a valid integer.");
    }

    return parsed;
}

var builder =
    Host.CreateApplicationBuilder(
        args);

var connectionString =
    Environment.GetEnvironmentVariable(
        "FLUXPAY_DB_CONNECTION");

if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString =
        $"Host=127.0.0.1;"
        + $"Port={RequiredEnvironmentVariable("POSTGRES_PORT")};"
        + $"Database={RequiredEnvironmentVariable("POSTGRES_DB")};"
        + $"Username={RequiredEnvironmentVariable("POSTGRES_USER")};"
        + $"Password={RequiredEnvironmentVariable("POSTGRES_PASSWORD")};"
        + "Include Error Detail=false";
}

var rabbitMqOptions =
    new RabbitMqOptions
    {
        HostName =
            RequiredEnvironmentVariable(
                "RABBITMQ_HOST"),

        Port =
            RequiredIntegerEnvironmentVariable(
                "RABBITMQ_PORT"),

        UserName =
            RequiredEnvironmentVariable(
                "RABBITMQ_USER"),

        Password =
            RequiredEnvironmentVariable(
                "RABBITMQ_PASSWORD"),

        VirtualHost =
            RequiredEnvironmentVariable(
                "RABBITMQ_VHOST"),

        ExchangeName =
            RabbitMqOptions.DefaultExchangeName,

        ClientProvidedName =
            "fluxpay-outbox-worker"
    };

rabbitMqOptions.Validate();

var workerOptions =
    new OutboxWorkerOptions
    {
        BatchSize =
            builder.Configuration.GetValue<int?>(
                $"{OutboxWorkerOptions.SectionName}:BatchSize")
            ?? 100,

        PollingIntervalMilliseconds =
            builder.Configuration.GetValue<int?>(
                $"{OutboxWorkerOptions.SectionName}:PollingIntervalMilliseconds")
            ?? 1000
    };

workerOptions.Validate();

var consumerOptions =
    new TransferCompletedConsumerOptions
    {
        ConsumerName =
            builder.Configuration.GetValue<string>(
                $"{TransferCompletedConsumerOptions.SectionName}:ConsumerName")
            ?? TransferCompletedConsumerOptions.DefaultConsumerName,

        QueueName =
            builder.Configuration.GetValue<string>(
                $"{TransferCompletedConsumerOptions.SectionName}:QueueName")
            ?? TransferCompletedConsumerOptions.DefaultQueueName,

        RetryExchangeName =
            builder.Configuration.GetValue<string>(
                $"{TransferCompletedConsumerOptions.SectionName}:RetryExchangeName")
            ?? TransferCompletedConsumerOptions.DefaultRetryExchangeName,

        DeadLetterExchangeName =
            builder.Configuration.GetValue<string>(
                $"{TransferCompletedConsumerOptions.SectionName}:DeadLetterExchangeName")
            ?? TransferCompletedConsumerOptions.DefaultDeadLetterExchangeName,

        DeadLetterQueueName =
            builder.Configuration.GetValue<string>(
                $"{TransferCompletedConsumerOptions.SectionName}:DeadLetterQueueName")
            ?? TransferCompletedConsumerOptions.DefaultDeadLetterQueueName,

        DeadLetterRoutingKey =
            builder.Configuration.GetValue<string>(
                $"{TransferCompletedConsumerOptions.SectionName}:DeadLetterRoutingKey")
            ?? TransferCompletedConsumerOptions.DefaultDeadLetterRoutingKey,

        ClientProvidedName =
            builder.Configuration.GetValue<string>(
                $"{TransferCompletedConsumerOptions.SectionName}:ClientProvidedName")
            ?? "fluxpay-transfer-completed-consumer",

        PrefetchCount =
            builder.Configuration.GetValue<int?>(
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
    IIntegrationEventPublisher,
    RabbitMqPublisher>();

builder.Services.AddScoped<
    OutboxProcessor>();

builder.Services.AddScoped<
    InboxMessageProcessor>();

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
            metrics
                .AddMeter(
                    WorkerMetrics.MeterName)
                .AddRuntimeInstrumentation()
                .AddOtlpExporter())
    .WithTracing(
        tracing =>
            tracing
                .AddSource(
                    MessagingActivitySource.Name)
                .AddOtlpExporter());

builder.Services.AddHostedService<
    Worker>();

builder.Services.AddHostedService<
    TransferCompletedRabbitMqConsumer>();

var host =
    builder.Build();

await host.RunAsync();
