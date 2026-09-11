using FluxPay.Infrastructure.Persistence.Outbox;
using FluxPay.Worker.Observability;

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
            "FluxPay outbox worker started. BatchSize={BatchSize}, PollingIntervalMilliseconds={PollingIntervalMilliseconds}, Parallelism={Parallelism}.",
            _options.BatchSize,
            _options.PollingIntervalMilliseconds,
            _options.Parallelism);

        try
        {
            var lanes =
                Enumerable
                    .Range(
                        1,
                        _options.Parallelism)
                    .Select(
                        laneNumber =>
                            RunLaneAsync(
                                laneNumber,
                                stoppingToken))
                    .ToArray();

            await Task.WhenAll(
                lanes);
        }
        finally
        {
            _logger.LogInformation(
                "FluxPay outbox worker stopped.");
        }
    }

    private async Task RunLaneAsync(
        int laneNumber,
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox processing lane {LaneNumber} started.",
            laneNumber);

        try
        {
            while (
                !stoppingToken
                    .IsCancellationRequested)
            {
                try
                {
                    var shouldDelay =
                        await ProcessOnceAsync(
                            laneNumber,
                            stoppingToken);

                    if (shouldDelay)
                    {
                        await Task.Delay(
                            _options
                                .PollingIntervalMilliseconds,
                            stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                    when (
                        stoppingToken
                            .IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Unexpected error while processing transactional outbox on lane {LaneNumber}.",
                        laneNumber);

                    await Task.Delay(
                        _options
                            .PollingIntervalMilliseconds,
                        stoppingToken);
                }
            }
        }
        finally
        {
            _logger.LogInformation(
                "Outbox processing lane {LaneNumber} stopped.",
                laneNumber);
        }
    }

    private async Task<bool> ProcessOnceAsync(
        int laneNumber,
        CancellationToken cancellationToken)
    {
        await using var scope =
            _scopeFactory
                .CreateAsyncScope();

        var processor =
            scope.ServiceProvider
                .GetRequiredService<
                    OutboxProcessor>();

        var result =
            await processor.ProcessBatchAsync(
                _options.BatchSize,
                cancellationToken);

        WorkerMetrics.RecordOutboxPublished(
            result.Published);

        WorkerMetrics.RecordOutboxPublishFailures(
            result.Failed);

        WorkerMetrics.RecordOutboxDeadLettered(
            result.DeadLettered);

        if (
            result.Candidates > 0
            || result.Failed > 0
            || result.DeadLettered > 0
            || result.Skipped > 0)
        {
            _logger.LogInformation(
                "Outbox batch processed. Lane={LaneNumber}, Candidates={Candidates}, Published={Published}, Failed={Failed}, DeadLettered={DeadLettered}, Skipped={Skipped}.",
                laneNumber,
                result.Candidates,
                result.Published,
                result.Failed,
                result.DeadLettered,
                result.Skipped);
        }

        if (result.Failed > 0)
        {
            _logger.LogWarning(
                "Outbox lane {LaneNumber} contains {Failed} failed publication attempt(s). Retry was scheduled according to the outbox backoff policy.",
                laneNumber,
                result.Failed);
        }

        if (result.DeadLettered > 0)
        {
            _logger.LogError(
                "Outbox lane {LaneNumber} dead-lettered {DeadLettered} message(s) after exhausting the publication retry policy.",
                laneNumber,
                result.DeadLettered);
        }

        return result.Candidates
            < _options.BatchSize;
    }
}
