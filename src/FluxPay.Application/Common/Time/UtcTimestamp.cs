namespace FluxPay.Application.Common.Time;

public static class UtcTimestamp
{
    private const long TicksPerMicrosecond =
        10;

    public static DateTimeOffset Normalize(
        DateTimeOffset value)
    {
        var utc =
            value.ToUniversalTime();

        var normalizedTicks =
            utc.Ticks
            - (
                utc.Ticks
                % TicksPerMicrosecond
            );

        return new DateTimeOffset(
            normalizedTicks,
            TimeSpan.Zero);
    }

    public static DateTimeOffset GetUtcNow(
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(
            timeProvider);

        return Normalize(
            timeProvider.GetUtcNow());
    }
}
