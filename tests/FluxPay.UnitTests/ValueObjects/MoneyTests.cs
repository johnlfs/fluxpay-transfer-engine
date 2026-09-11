using FluxPay.Domain.Common;
using FluxPay.Domain.ValueObjects;

namespace FluxPay.UnitTests.ValueObjects;

public sealed class MoneyTests
{
    [Fact]
    public void Constructor_WithValidAmount_ShouldCreateMoney()
    {
        var money = new Money(123.45m);

        Assert.Equal(123.45m, money.Amount);
    }

    [Fact]
    public void Constructor_WithZero_ShouldCreateMoney()
    {
        var money = new Money(0m);

        Assert.Equal(0m, money.Amount);
    }

    [Fact]
    public void Constructor_WithNegativeAmount_ShouldThrow()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => new Money(-0.01m));

        Assert.Equal(
            "Money amount cannot be negative.",
            exception.Message);
    }

    [Fact]
    public void Constructor_WithMoreThanTwoDecimalPlaces_ShouldThrow()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => new Money(10.001m));

        Assert.Equal(
            "Money amount cannot have more than two decimal places.",
            exception.Message);
    }
}
