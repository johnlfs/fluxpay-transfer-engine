namespace FluxPay.Application.Messaging.Retry;

public static class ConsumerRetryPolicy
{
    public const int MaximumAttempts =
        4;

    public static bool IsExhausted(
        int attemptCount)
    {
        if (attemptCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attemptCount),
                attemptCount,
                "Attempt count must be greater than zero.");
        }

        return attemptCount
            >= MaximumAttempts;
    }

    public static TimeSpan GetDelay(
        int attemptCount)
    {
        if (IsExhausted(attemptCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(attemptCount),
                attemptCount,
                $"Retry delay is defined only for attempts 1 through {MaximumAttempts - 1}.");
        }

        return attemptCount switch
        {
            1 =>
                TimeSpan.FromSeconds(
                    5),

            2 =>
                TimeSpan.FromSeconds(
                    15),

            3 =>
                TimeSpan.FromSeconds(
                    45),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(attemptCount),
                    attemptCount,
                    $"Retry delay is defined only for attempts 1 through {MaximumAttempts - 1}.")
        };
    }
}
