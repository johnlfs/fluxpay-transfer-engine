using System.Diagnostics.Metrics;

namespace FluxPay.Worker.Observability;

public static class WorkerMetrics
{
    public const string MeterName =
        "FluxPay.Worker";

    private static readonly Meter Meter =
        new(
            MeterName);

    private static readonly Counter<long> OutboxPublished =
        Meter.CreateCounter<long>(
            name:
                "fluxpay.outbox.published",
            unit:
                "{message}",
            description:
                "Number of outbox messages successfully published.");

    private static readonly Counter<long> OutboxPublishFailures =
        Meter.CreateCounter<long>(
            name:
                "fluxpay.outbox.publish_failures",
            unit:
                "{attempt}",
            description:
                "Number of failed outbox publication attempts.");

    private static readonly Counter<long> OutboxDeadLettered =
        Meter.CreateCounter<long>(
            name:
                "fluxpay.outbox.dead_lettered",
            unit:
                "{message}",
            description:
                "Number of outbox messages dead-lettered after exhausting publication retries.");

    private static readonly Counter<long> ConsumerProcessed =
        Meter.CreateCounter<long>(
            name:
                "fluxpay.consumer.processed",
            unit:
                "{message}",
            description:
                "Number of consumer messages successfully processed.");

    private static readonly Counter<long> ConsumerDuplicates =
        Meter.CreateCounter<long>(
            name:
                "fluxpay.consumer.duplicates",
            unit:
                "{message}",
            description:
                "Number of duplicate consumer deliveries detected by the inbox.");

    private static readonly Counter<long> ConsumerRetries =
        Meter.CreateCounter<long>(
            name:
                "fluxpay.consumer.retries",
            unit:
                "{message}",
            description:
                "Number of consumer messages successfully scheduled for retry.");

    private static readonly Counter<long> ConsumerDeadLettered =
        Meter.CreateCounter<long>(
            name:
                "fluxpay.consumer.dead_lettered",
            unit:
                "{message}",
            description:
                "Number of consumer messages sent to the dead-letter flow.");

    public static void RecordOutboxPublished(
        int count)
    {
        if (count <= 0)
        {
            return;
        }

        OutboxPublished.Add(
            count);
    }

    public static void RecordOutboxPublishFailures(
        int count)
    {
        if (count <= 0)
        {
            return;
        }

        OutboxPublishFailures.Add(
            count);
    }

    public static void RecordOutboxDeadLettered(
        int count)
    {
        if (count <= 0)
        {
            return;
        }

        OutboxDeadLettered.Add(
            count);
    }

    public static void RecordConsumerProcessed()
    {
        ConsumerProcessed.Add(
            1);
    }

    public static void RecordConsumerDuplicate()
    {
        ConsumerDuplicates.Add(
            1);
    }

    public static void RecordConsumerRetry(
        int failedAttemptCount,
        TimeSpan delay)
    {
        ConsumerRetries.Add(
            1,
            new KeyValuePair<string, object?>(
                "attempt",
                failedAttemptCount),
            new KeyValuePair<string, object?>(
                "delay_seconds",
                checked(
                    (int)
                        delay.TotalSeconds)));
    }

    public static void RecordConsumerDeadLettered(
        string reason)
    {
        ConsumerDeadLettered.Add(
            1,
            new KeyValuePair<string, object?>(
                "reason",
                reason));
    }
}
