# Entities and value objects

## Entities: defined by identity

An entity is something you track over time. It changes, but it stays the same thing: order
`7f3a…` is the same order whether it is a draft or shipped.

- Equality is by id, never by values. Two orders with identical lines are still two orders.
- The id is assigned at creation and never changes.
- State changes through behaviour (see [rich-domain-model.md](rich-domain-model.md)).

The base class [Entity.cs](../src/BuildingBlocks/BuildingBlocks.Domain/Entity.cs) implements
identity-based equality once. Examples: `Order`, `OrderLine`, `Shipment`.

### Strongly typed ids

```csharp
public sealed record OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.NewGuid());
}
```

A `Guid` parameter accepts any `Guid`; an `OrderId` parameter does not accept a `CustomerId`. This
removes a whole class of bugs for the price of one line per id. EF Core stores it as a plain column
with a value converter, see [OrderingDbContext.cs](../src/Ordering/Ordering.Infrastructure/OrderingDbContext.cs).

## Value objects: defined by their values

A value object describes something; it has no identity of its own. Two `Money(12.50, "EUR")` are
interchangeable.

- **Immutable.** Operations return a new value: `money.Add(other)` leaves `money` untouched.
- **Equal by value.** C# `record` types give this for free.
- **Valid by construction.** The constructor enforces the rules, so an invalid value object cannot
  exist and nobody downstream has to check again.

```csharp
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

    public Money Add(Money other)
    {
        Invariant.Require(other.Currency == Currency, $"Cannot add {other.Currency} to {Currency}.");
        return new(Amount + other.Amount, Currency);
    }
}
```

The sample's value objects: `Money`, `Quantity`, `ProductCode`, `ShippingAddress` in
[Ordering](../src/Ordering/Ordering.Domain/Orders/ValueObjects.cs); `DeliveryAddress`,
`TrackingCode`, `ShipmentItem` in [Shipping](../src/Shipping/Shipping.Domain/Shipments/ValueObjects.cs).

## How to choose

Ask: *if I swapped this for another one with the same values, would anyone care?*

| | Entity | Value object |
|---|---|---|
| `Order` | yes: it has a history, customers refer to it | |
| `OrderLine` | yes: lines are addressed individually inside the order | |
| `ShipmentItem` | | no: two items "BOOK-1984 × 2" are interchangeable |
| `Money`, `Quantity`, `Address` | | no |

The same concept can be an entity in one context and a value object in another. Ordering's line is
an entity; Shipping's item is a value object. Choose per context.

Prefer value objects. They are easier to test, safe to share, and they pull rules out of entities
into small, focused types. A good sign: primitive parameters (`decimal amount, string currency`)
disappear from your method signatures.

## Persisting them with EF Core

| Kind | Mapping |
|---|---|
| Single-value object (`OrderId`, `ProductCode`, `Quantity`) | `HasConversion(v => v.Value, v => new X(v))` |
| Multi-value object (`Money`, `ShippingAddress`) | `OwnsOne`, columns in the owner's table |
| Collection of entities inside an aggregate (`OrderLine`) | `OwnsMany` with its own key |
| Collection of value objects (`ShipmentItem`) | `OwnsMany` with a shadow key the domain never sees |

Two things EF Core will not do for you: map get-only properties by convention (list each one
explicitly, see [pitfalls.md](pitfalls.md#get-only-properties-are-not-mapped)), and call your
validating constructor for converted values when reading invalid legacy data — it will, and it will
throw. Clean legacy data before you tighten a value object.
