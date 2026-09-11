using FluxPay.Infrastructure.Persistence.Outbox;

namespace FluxPay.Worker;

public sealed class Worker
    : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxWorkerOptions _options;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IServiceScopeFactory scopeFactory,
        OutboxWorkerOptions options,
        ILogger<Worker> logger)
    {
        _scopeFactory =
            scopeFactory;

        _options =
            options;

        _logger =
            logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "FluxPay outbox worker started. BatchSize={BatchSize}, PollingIntervalMilliseconds={PollingIntervalMilliseconds}.",
            _options.BatchSize,
            _options.PollingIntervalMilliseconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var shouldDelay =
                        await ProcessOnceAsync(
                            stoppingToken);

                    if (shouldDelay)
                    {
                        await Task.Delay(
                            _options.PollingIntervalMilliseconds,
                            stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Unexpected error while processing the transactional outbox.");

                    await Task.Delay(
                        _options.PollingIntervalMilliseconds,
                        stoppingToken);
                }
            }
        }
        finally
        {
            _logger.LogInformation(
                "FluxPay outbox worker stopped.");
        }
    }

    private async Task<bool> ProcessOnceAsync(
        CancellationToken cancellationToken)
    {
        await using var scope =
            _scopeFactory.CreateAsyncScope();

        var processor =
            scope.ServiceProvider
                .GetRequiredService<OutboxProcessor>();

        var result =
            await processor.ProcessBatchAsync(
                _options.BatchSize,
                cancellationToken);

        if (
            result.Candidates > 0
            || result.Failed > 0
            || result.Skipped > 0)
        {
            _logger.LogInformation(
                "Outbox batch processed. Candidates={Candidates}, Published={Published}, Failed={Failed}, Skipped={Skipped}.",
                result.Candidates,
                result.Published,
                result.Failed,
                result.Skipped);
        }

        if (result.Failed > 0)
        {
            _logger.LogWarning(
                "Outbox batch contains {Failed} failed publication attempt(s). Messages remain pending for retry.",
                result.Failed);
        }

        return result.Candidates < _options.BatchSize;
    }
}
