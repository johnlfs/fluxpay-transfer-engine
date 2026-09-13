using FluxPay.Domain.Accounts;
using FluxPay.Domain.Common;
using FluxPay.Domain.ValueObjects;

namespace FluxPay.UnitTests.Accounts;

public sealed class AccountTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidData_ShouldCreateAccount()
    {
        var account = Account.Create(
            "000123",
            "Ada Lovelace",
            new Money(1000m),
            InitialTime);

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal("000123", account.AccountNumber);
        Assert.Equal("Ada Lovelace", account.OwnerName);
        Assert.Equal(1000m, account.Balance.Amount);
        Assert.Equal(InitialTime, account.CreatedAt);
        Assert.Equal(InitialTime, account.UpdatedAt);
    }

    [Fact]
    public void Create_ShouldTrimAccountNumberAndOwnerName()
    {
        var account = Account.Create(
            "  000123  ",
            "  Ada Lovelace  ",
            Money.Zero,
            InitialTime);

        Assert.Equal("000123", account.AccountNumber);
        Assert.Equal("Ada Lovelace", account.OwnerName);
    }

    [Fact]
    public void Create_WithBlankAccountNumber_ShouldThrow()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => Account.Create(
                "   ",
                "Ada Lovelace",
                Money.Zero,
                InitialTime));

        Assert.Equal(
            "Account number is required.",
            exception.Message);
    }

    [Fact]
    public void Create_WithBlankOwnerName_ShouldThrow()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => Account.Create(
                "000123",
                "   ",
                Money.Zero,
                InitialTime));

        Assert.Equal(
            "Account owner name is required.",
            exception.Message);
    }

    [Fact]
    public void Debit_WithAvailableBalance_ShouldDecreaseBalance()
    {
        var account = CreateAccountWithBalance(1000m);

        var operationTime = InitialTime.AddMinutes(1);

        account.Debit(
            new Money(100m),
            operationTime);

        Assert.Equal(900m, account.Balance.Amount);
        Assert.Equal(operationTime, account.UpdatedAt);
    }

    [Fact]
    public void Debit_WithInsufficientBalance_ShouldThrowAndPreserveBalance()
    {
        var account = CreateAccountWithBalance(100m);

        var exception = Assert.Throws<InsufficientFundsException>(
            () => account.Debit(
                new Money(100.01m),
                InitialTime.AddMinutes(1)));

        Assert.Equal(100m, exception.AvailableBalance);
        Assert.Equal(100.01m, exception.RequestedAmount);
        Assert.Equal(100m, account.Balance.Amount);
        Assert.Equal(InitialTime, account.UpdatedAt);
    }

    [Fact]
    public void Debit_WithZeroAmount_ShouldThrow()
    {
        var account = CreateAccountWithBalance(100m);

        var exception = Assert.Throws<DomainValidationException>(
            () => account.Debit(
                Money.Zero,
                InitialTime.AddMinutes(1)));

        Assert.Equal(
            "Transaction amount must be greater than zero.",
            exception.Message);

        Assert.Equal(100m, account.Balance.Amount);
    }

    [Fact]
    public void Credit_WithValidAmount_ShouldIncreaseBalance()
    {
        var account = CreateAccountWithBalance(100m);

        var operationTime = InitialTime.AddMinutes(1);

        account.Credit(
            new Money(50m),
            operationTime);

        Assert.Equal(150m, account.Balance.Amount);
        Assert.Equal(operationTime, account.UpdatedAt);
    }

    [Fact]
    public void Credit_WithZeroAmount_ShouldThrow()
    {
        var account = CreateAccountWithBalance(100m);

        var exception = Assert.Throws<DomainValidationException>(
            () => account.Credit(
                Money.Zero,
                InitialTime.AddMinutes(1)));

        Assert.Equal(
            "Transaction amount must be greater than zero.",
            exception.Message);

        Assert.Equal(100m, account.Balance.Amount);
    }

    [Fact]
    public void Create_WithTooLongAccountNumber_ShouldThrow()
    {
        var accountNumber =
            new string(
                'A',
                Account.MaximumAccountNumberLength
                + 1);

        Assert.Throws<DomainValidationException>(
            () =>
                Account.Create(
                    accountNumber,
                    "Ada Lovelace",
                    Money.Zero,
                    InitialTime));
    }

    [Fact]
    public void Create_WithTooLongOwnerName_ShouldThrow()
    {
        var ownerName =
            new string(
                'A',
                Account.MaximumOwnerNameLength
                + 1);

        Assert.Throws<DomainValidationException>(
            () =>
                Account.Create(
                    "000123",
                    ownerName,
                    Money.Zero,
                    InitialTime));
    }

    [Fact]
    public void Credit_WhenResultExceedsMaximum_ShouldThrowAndPreserveBalance()
    {
        var originalBalance =
            Money.MaximumAmount
            - 1m;

        var account =
            CreateAccountWithBalance(
                originalBalance);

        Assert.Throws<DomainValidationException>(
            () =>
                account.Credit(
                    new Money(
                        2m),
                    InitialTime.AddMinutes(
                        1)));

        Assert.Equal(
            originalBalance,
            account.Balance.Amount);

        Assert.Equal(
            InitialTime,
            account.UpdatedAt);
    }

    private static Account CreateAccountWithBalance(decimal balance)
    {
        return Account.Create(
            "000123",
            "Ada Lovelace",
            new Money(balance),
            InitialTime);
    }
}
