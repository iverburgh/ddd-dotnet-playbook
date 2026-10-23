# Invariants

An invariant is a rule that must always be true for an object to be meaningful. "A quantity is
between 1 and 1000." "A placed order has at least one line." The domain enforces them so no caller
has to remember them.

## Two kinds of "no"

Not every refusal is the same. The sample distinguishes two, and handles them differently.

| | Invalid input | Business rule |
|---|---|---|
| Example | quantity 0, currency `euro`, empty street | adding a line to a placed order |
| Could it ever succeed? | never, in any state | yes, if the aggregate were in another state |
| Detected by | constructor of a value object | method of an aggregate |
| Mechanism | `Invariant.Require` throws `InvariantViolationException` | returns `Result.Failure(error)` |
| HTTP | `400 Bad Request` | `422 Unprocessable Entity`, with the rule's code |
| Expected in normal use? | no, a client bug or bad input | yes, users try things |

### Invalid input: throw

[Invariant.cs](../src/BuildingBlocks/BuildingBlocks.Domain/Invariant.cs):

```csharp
public sealed record Quantity
{
    public Quantity(int value)
    {
        Invariant.Require(value is >= 1 and <= Max, $"A quantity is between 1 and {Max}, not {value}.");
        Value = value;
    }
}
```

A value object that would be invalid simply does not get created. Everything that receives a
`Quantity` can trust it.

### Business rule: return a result

[Result.cs](../src/BuildingBlocks/BuildingBlocks.Domain/Result.cs), used in [Order.cs](../src/Ordering/Ordering.Domain/Orders/Order.cs):

```csharp
public Result Cancel(string reason, DateTimeOffset now)
{
    switch (Status)
    {
        case OrderStatus.Cancelled: return Result.Failure(OrderErrors.AlreadyCancelled);
        case OrderStatus.Shipped:   return Result.Failure(OrderErrors.AlreadyShipped);
    }
    ...
}
```

The return type forces the caller to deal with the refusal. Each `Error` has a stable code
(`order.already-shipped`) that clients and support can rely on, and a message for people. All codes
of an aggregate live in one place (`OrderErrors`), which doubles as documentation of its rules.

## Where invariants live

| Scope | Enforced by | Example |
|---|---|---|
| One value | value object constructor | `Money` is non-negative with at most two decimals |
| One entity | entity method | an order line's quantity only grows within bounds |
| The whole aggregate | the root | at most 20 lines; all prices in the order's currency |
| Across aggregates | **not an invariant**: a process (events), or a unique index as a last line of defence | one shipment per order: domain check plus unique index on `Shipments.OrderId` |

If you find a rule that must hold *immediately* across two aggregates, the aggregate boundary is
probably wrong. See [aggregates.md](aggregates.md#designing-aggregate-boundaries).

## Rules of thumb

- **Check before you change.** A method validates everything first and only then mutates, so a
  refusal leaves the aggregate untouched (and raises no event). The tests check this, for example
  `An_empty_order_cannot_be_placed_and_raises_nothing`.
- **Do not validate twice.** Once a value is a `Quantity`, nothing re-checks its range.
- **No public setters, no public constructors that skip the rules.** The ORM uses a private
  parameterless constructor.
- **Keep the database constraints too.** A `NOT NULL`, a maximum length or a unique index catches
  bugs in code that bypasses the domain — a migration script, a manual fix. They complement the
  domain; they do not replace it.
