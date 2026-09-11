using FluxPay.Application.Messaging.Retry;

namespace FluxPay.UnitTests.Application.Messaging;

public sealed class ConsumerRetryPolicyTests
{
    [Theory]
    [InlineData(
        1,
        5)]
    [InlineData(
        2,
        15)]
    [InlineData(
        3,
        45)]
    public void GetDelay_ForRetryableAttempt_ReturnsExpectedDelay(
        int attemptCount,
        int expectedSeconds)
    {
        var delay =
            ConsumerRetryPolicy.GetDelay(
                attemptCount);

        Assert.Equal(
            TimeSpan.FromSeconds(
                expectedSeconds),
            delay);
    }

    [Theory]
    [InlineData(
        1,
        false)]
    [InlineData(
        2,
        false)]
    [InlineData(
        3,
        false)]
    [InlineData(
        4,
        true)]
    [InlineData(
        5,
        true)]
    public void IsExhausted_ReturnsExpectedResult(
        int attemptCount,
        bool expected)
    {
        var result =
            ConsumerRetryPolicy.IsExhausted(
                attemptCount);

        Assert.Equal(
            expected,
            result);
    }

    [Fact]
    public void IsExhausted_WithZeroAttempt_Throws()
    {
        var exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    ConsumerRetryPolicy.IsExhausted(
                        0);
                });

        Assert.Contains(
            "greater than zero",
            exception.Message);
    }

    [Fact]
    public void IsExhausted_WithNegativeAttempt_Throws()
    {
        var exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    ConsumerRetryPolicy.IsExhausted(
                        -1);
                });

        Assert.Contains(
            "greater than zero",
            exception.Message);
    }

    [Fact]
    public void GetDelay_WhenMaximumAttemptsWasReached_Throws()
    {
        var exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    ConsumerRetryPolicy.GetDelay(
                        ConsumerRetryPolicy.MaximumAttempts);
                });

        Assert.Contains(
            "Retry delay is defined only",
            exception.Message);
    }

    [Fact]
    public void GetDelay_WithZeroAttempt_Throws()
    {
        var exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    ConsumerRetryPolicy.GetDelay(
                        0);
                });

        Assert.Contains(
            "greater than zero",
            exception.Message);
    }
}
