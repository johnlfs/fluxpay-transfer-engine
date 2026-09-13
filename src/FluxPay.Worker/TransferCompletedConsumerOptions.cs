namespace FluxPay.Worker;

public sealed class TransferCompletedConsumerOptions
{
    public const string SectionName =
        "TransferCompletedConsumer";

    public const string RetryTopologyVersion =
        "v1";

    public const string DefaultConsumerName =
        "transfer-completed-consumer";

    public const string DefaultQueueName =
        "fluxpay.transfer-completed";

    public const string DefaultRetryExchangeName =
        "fluxpay.retry";

    public const string DefaultDeadLetterExchangeName =
        "fluxpay.dead-letter";

    public const string DefaultDeadLetterQueueName =
        "fluxpay.transfer-completed.dlq";

    public const string DefaultDeadLetterRoutingKey =
        "transfer.completed.dlq";

    public string ConsumerName { get; init; } =
        DefaultConsumerName;

    public string QueueName { get; init; } =
        DefaultQueueName;

    public string RetryExchangeName { get; init; } =
        DefaultRetryExchangeName;

    public string DeadLetterExchangeName { get; init; } =
        DefaultDeadLetterExchangeName;

    public string DeadLetterQueueName { get; init; } =
        DefaultDeadLetterQueueName;

    public string DeadLetterRoutingKey { get; init; } =
        DefaultDeadLetterRoutingKey;

    public string ClientProvidedName { get; init; } =
        "fluxpay-transfer-completed-consumer";

    public int PrefetchCount { get; init; } =
        1;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConsumerName))
        {
            throw new ArgumentException(
                "Consumer name is required.",
                nameof(ConsumerName));
        }

        if (string.IsNullOrWhiteSpace(QueueName))
        {
            throw new ArgumentException(
                "Consumer queue name is required.",
                nameof(QueueName));
        }

        if (string.IsNullOrWhiteSpace(RetryExchangeName))
        {
            throw new ArgumentException(
                "Retry exchange name is required.",
                nameof(RetryExchangeName));
        }

        if (string.IsNullOrWhiteSpace(DeadLetterExchangeName))
        {
            throw new ArgumentException(
                "Dead-letter exchange name is required.",
                nameof(DeadLetterExchangeName));
        }

        if (string.IsNullOrWhiteSpace(DeadLetterQueueName))
        {
            throw new ArgumentException(
                "Dead-letter queue name is required.",
                nameof(DeadLetterQueueName));
        }

        if (string.IsNullOrWhiteSpace(DeadLetterRoutingKey))
        {
            throw new ArgumentException(
                "Dead-letter routing key is required.",
                nameof(DeadLetterRoutingKey));
        }

        if (string.IsNullOrWhiteSpace(ClientProvidedName))
        {
            throw new ArgumentException(
                "RabbitMQ client-provided name is required.",
                nameof(ClientProvidedName));
        }

        if (
            PrefetchCount <= 0
            || PrefetchCount > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PrefetchCount),
                PrefetchCount,
                "Consumer prefetch count must be between 1 and 1000.");
        }
    }
}
