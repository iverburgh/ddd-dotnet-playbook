using BuildingBlocks.Domain;
using Ordering.Domain.Orders;

namespace Ordering.Domain.Tests;

public sealed class ValueObjectTests
{
    [Test]
    public void Money_with_the_same_amount_and_currency_is_equal() =>
        Assert.That(new Money(10.50m, "EUR"), Is.EqualTo(new Money(10.5m, "EUR")));

    [Test]
    public void Money_in_different_currencies_is_not_equal() =>
        Assert.That(new Money(10m, "EUR"), Is.Not.EqualTo(new Money(10m, "USD")));

    [TestCase(-0.01)]
    [TestCase(1.005)]
    public void Money_rejects_impossible_amounts(decimal amount) =>
        Assert.Throws<InvariantViolationException>(() => _ = new Money(amount, "EUR"));

    [TestCase("")]
    [TestCase("eur")]
    [TestCase("EURO")]
    public void Money_rejects_invalid_currencies(string currency) =>
        Assert.Throws<InvariantViolationException>(() => _ = new Money(1m, currency));

    [Test]
    public void Money_in_different_currencies_cannot_be_added() =>
        Assert.Throws<InvariantViolationException>(() => new Money(1m, "EUR").Add(new Money(1m, "USD")));

    [Test]
    public void Adding_money_returns_a_new_value_and_leaves_the_original_alone()
    {
        var ten = new Money(10m, "EUR");

        var sum = ten.Add(new Money(5m, "EUR"));

        Assert.That(sum, Is.EqualTo(new Money(15m, "EUR")));
        Assert.That(ten, Is.EqualTo(new Money(10m, "EUR")));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(Quantity.Max + 1)]
    public void Quantity_is_between_one_and_the_maximum(int value) =>
        Assert.Throws<InvariantViolationException>(() => _ = new Quantity(value));

    [TestCase("")]
    [TestCase("book-1984")]
    [TestCase("BOOK 1984")]
    public void ProductCode_rejects_invalid_codes(string value) =>
        Assert.Throws<InvariantViolationException>(() => _ = new ProductCode(value));

    [Test]
    public void ShippingAddress_requires_a_two_letter_country_code() =>
        Assert.Throws<InvariantViolationException>(() => _ = new ShippingAddress("Main Street 1", "1234 AB", "Springfield", "NLD"));
}
