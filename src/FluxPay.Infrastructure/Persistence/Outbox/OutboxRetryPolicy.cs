namespace FluxPay.Infrastructure.Persistence.Outbox;

public static class OutboxRetryPolicy
{
    public const int MaximumAttempts =
        5;

    public static TimeSpan GetDelay(
        int attemptCount)
    {
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

            4 =>
                TimeSpan.FromSeconds(
                    135),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(attemptCount),
                    attemptCount,
                    "Retry delay is defined only for attempts 1 through 4.")
        };
    }
}
