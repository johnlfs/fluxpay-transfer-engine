namespace FluxPay.Worker;

public sealed class OutboxWorkerOptions
{
    public const string SectionName =
        "OutboxWorker";

    public int BatchSize { get; init; } =
        100;

    public int PollingIntervalMilliseconds { get; init; } =
        1000;

    public void Validate()
    {
        if (
            BatchSize <= 0
            || BatchSize > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BatchSize),
                BatchSize,
                "Outbox batch size must be between 1 and 1000.");
        }

        if (
            PollingIntervalMilliseconds < 100
            || PollingIntervalMilliseconds > 60000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PollingIntervalMilliseconds),
                PollingIntervalMilliseconds,
                "Outbox polling interval must be between 100 and 60000 milliseconds.");
        }
    }
}
