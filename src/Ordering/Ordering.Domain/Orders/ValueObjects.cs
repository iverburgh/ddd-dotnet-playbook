using BuildingBlocks.Domain;

namespace Ordering.Domain.Orders;

public sealed record OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public sealed record OrderLineId(Guid Value)
{
    public static OrderLineId New() => new(Guid.NewGuid());
}

public sealed record CustomerId(Guid Value);

/// <summary>The code of a product in the catalogue, such as <c>BOOK-1984</c>.</summary>
public sealed record ProductCode
{
    public ProductCode(string value)
    {
        Invariant.Require(
            value is { Length: >= 1 and <= 32 } && value.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '-'),
            $"A product code is 1 to 32 characters: capitals, digits and hyphens. '{value}' is not.");
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>How many of a product an order line contains.</summary>
public sealed record Quantity
{
    public const int Max = 1000;

    public Quantity(int value)
    {
        Invariant.Require(value is >= 1 and <= Max, $"A quantity is between 1 and {Max}, not {value}.");
        Value = value;
    }

    public int Value { get; }

    public static bool CanAdd(Quantity left, Quantity right) => left.Value + right.Value <= Max;

    public Quantity Add(Quantity other) => new(Value + other.Value);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>An amount in one currency. Amounts in different currencies never mix.</summary>
public sealed record Money
{
    public Money(decimal amount, string currency)
    {
        Invariant.Require(amount >= 0, $"An amount of money cannot be negative: {amount}.");
        Invariant.Require(decimal.Round(amount, 2) == amount, $"An amount of money has at most two decimals: {amount}.");
        Invariant.Require(IsValidCurrency(currency), $"A currency is a three-letter ISO 4217 code such as EUR, not '{currency}'.");
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero(string currency) => new(0, currency);

    public static bool IsValidCurrency(string? currency) => currency is { Length: 3 } && currency.All(char.IsAsciiLetterUpper);

    public Money Add(Money other)
    {
        Invariant.Require(other.Currency == Currency, $"Cannot add {other.Currency} to {Currency}.");
        return new(Amount + other.Amount, Currency);
    }

    public Money Multiply(int factor) => new(Amount * factor, Currency);

    public override string ToString() => $"{Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} {Currency}";
}

/// <summary>Where the customer wants the order delivered.</summary>
public sealed record ShippingAddress
{
    public ShippingAddress(string street, string postalCode, string city, string countryCode)
    {
        Invariant.Require(!string.IsNullOrWhiteSpace(street), "A shipping address needs a street.");
        Invariant.Require(!string.IsNullOrWhiteSpace(postalCode), "A shipping address needs a postal code.");
        Invariant.Require(!string.IsNullOrWhiteSpace(city), "A shipping address needs a city.");
        Invariant.Require(countryCode is { Length: 2 } && countryCode.All(char.IsAsciiLetterUpper), $"A country code is two capitals such as NL, not '{countryCode}'.");
        Street = street.Trim();
        PostalCode = postalCode.Trim();
        City = city.Trim();
        CountryCode = countryCode;
    }

    public string Street { get; }

    public string PostalCode { get; }

    public string City { get; }

    public string CountryCode { get; }
}
