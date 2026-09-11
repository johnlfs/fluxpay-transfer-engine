using FluxPay.Application.Common.Time;

namespace FluxPay.UnitTests.Application.Common.Time;

public sealed class UtcTimestampTests
{
    [Fact]
    public void Normalize_WithSubMicrosecondPrecision_TruncatesToMicroseconds()
    {
        var baseTimestamp =
            new DateTimeOffset(
                2026,
                9,
                11,
                7,
                9,
                9,
                TimeSpan.Zero);

        var timestamp =
            baseTimestamp.AddTicks(
                2_607_132);

        var result =
            UtcTimestamp.Normalize(
                timestamp);

        var expected =
            baseTimestamp.AddTicks(
                2_607_130);

        Assert.Equal(
            expected,
            result);

        Assert.Equal(
            TimeSpan.Zero,
            result.Offset);

        Assert.Equal(
            0,
            result.Ticks % 10);
    }

    [Fact]
    public void Normalize_WithNonUtcOffset_ConvertsToUtcAndPreservesInstant()
    {
        var timestamp =
            new DateTimeOffset(
                2026,
                9,
                11,
                10,
                9,
                9,
                260,
                TimeSpan.FromHours(
                    3))
            .AddTicks(
                7_132);

        var result =
            UtcTimestamp.Normalize(
                timestamp);

        Assert.Equal(
            TimeSpan.Zero,
            result.Offset);

        Assert.Equal(
            0,
            result.Ticks % 10);

        Assert.Equal(
            timestamp
                .ToUniversalTime()
                .Ticks
                / 10,
            result.Ticks
                / 10);
    }

    [Fact]
    public void Normalize_WithMicrosecondPrecision_DoesNotChangeTimestamp()
    {
        var timestamp =
            new DateTimeOffset(
                2026,
                9,
                11,
                7,
                9,
                9,
                TimeSpan.Zero)
            .AddTicks(
                2_607_130);

        var result =
            UtcTimestamp.Normalize(
                timestamp);

        Assert.Equal(
            timestamp,
            result);
    }

    [Fact]
    public void GetUtcNow_NormalizesTimeProviderValue()
    {
        var timestamp =
            new DateTimeOffset(
                2026,
                9,
                11,
                7,
                9,
                9,
                TimeSpan.Zero)
            .AddTicks(
                5_363_642);

        var timeProvider =
            new FixedTimeProvider(
                timestamp);

        var result =
            UtcTimestamp.GetUtcNow(
                timeProvider);

        Assert.Equal(
            timestamp.Ticks - 2,
            result.Ticks);

        Assert.Equal(
            TimeSpan.Zero,
            result.Offset);

        Assert.Equal(
            0,
            result.Ticks % 10);
    }

    private sealed class FixedTimeProvider
        : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow =
                utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
