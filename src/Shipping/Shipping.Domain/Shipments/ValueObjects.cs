using BuildingBlocks.Domain;

namespace Shipping.Domain.Shipments;

public sealed record ShipmentId(Guid Value)
{
    public static ShipmentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Shipping's view of an order: just which one. Shipping does not know or care what an order looks like inside Ordering.</summary>
public sealed record OrderReference(Guid Value)
{
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A product and how many go in the box. Unlike an order line this has no identity of its own:
/// two items with the same product and quantity are interchangeable, so it is a value object.
/// </summary>
public sealed record ShipmentItem
{
    public ShipmentItem(string productCode, int quantity)
    {
        Invariant.Require(!string.IsNullOrWhiteSpace(productCode), "A shipment item needs a product code.");
        Invariant.Require(quantity > 0, $"A shipment item needs a positive quantity, not {quantity}.");
        ProductCode = productCode;
        Quantity = quantity;
    }

    public string ProductCode { get; }

    public int Quantity { get; }
}

/// <summary>Where the carrier delivers. Ordering calls a similar concept a shipping address; each context owns its own model.</summary>
public sealed record DeliveryAddress
{
    public DeliveryAddress(string street, string postalCode, string city, string countryCode)
    {
        Invariant.Require(!string.IsNullOrWhiteSpace(street), "A delivery address needs a street.");
        Invariant.Require(!string.IsNullOrWhiteSpace(postalCode), "A delivery address needs a postal code.");
        Invariant.Require(!string.IsNullOrWhiteSpace(city), "A delivery address needs a city.");
        Invariant.Require(countryCode is { Length: 2 }, $"A country code has two letters, not '{countryCode}'.");
        Street = street;
        PostalCode = postalCode;
        City = city;
        CountryCode = countryCode;
    }

    public string Street { get; }

    public string PostalCode { get; }

    public string City { get; }

    public string CountryCode { get; }
}

/// <summary>The carrier's reference for a parcel: 8 to 30 capitals and digits.</summary>
public sealed record TrackingCode
{
    public TrackingCode(string value)
    {
        Invariant.Require(
            value is { Length: >= 8 and <= 30 } && value.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)),
            $"A tracking code is 8 to 30 capitals and digits, not '{value}'.");
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
