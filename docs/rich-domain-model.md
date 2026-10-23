# Rich domain model

Business rules live in the domain objects that own the data, not in services around them.

## Anemic versus rich

An anemic model is data with getters and setters; the rules are somewhere else:

```csharp
// Anemic: anyone can put the order in any state, and every caller must remember the rules.
public class Order
{
    public OrderStatus Status { get; set; }
    public List<OrderLine> Lines { get; set; } = new();
}

public class OrderService
{
    public void Place(Order order)
    {
        if (order.Lines.Count == 0) throw new Exception("Empty");
        order.Status = OrderStatus.Placed;   // and the next service forgets the check
    }
}
```

A rich model exposes behaviour; its state can only change through methods that enforce the rules:

```csharp
public sealed class Order : AggregateRoot<OrderId>
{
    public OrderStatus Status { get; private set; }
    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    public Result Place(ShippingAddress shippingAddress, DateTimeOffset now)
    {
        if (Status != OrderStatus.Draft) return Result.Failure(OrderErrors.NotDraft(Status));
        if (_lines.Count == 0) return Result.Failure(OrderErrors.Empty);

        Status = OrderStatus.Placed;
        ...
        Raise(new OrderPlaced(...));
        return Result.Success();
    }
}
```

The full version is [Order.cs](../src/Ordering/Ordering.Domain/Orders/Order.cs).

## The techniques

| Technique | Why | In the sample |
|---|---|---|
| Private setters | state changes only through behaviour | every property of `Order` and `Shipment` |
| Read-only collections | callers cannot add a line behind the aggregate's back | `Lines => _lines.AsReadOnly()` |
| Factory methods with a name | creation is a business act with its own rules | `Order.Start`, `Shipment.Prepare` |
| A private parameterless constructor | for the ORM only; domain code cannot create an invalid object | `private Order()` |
| Methods named after business acts | the [ubiquitous language](ubiquitous-language.md) | `Place`, `Cancel`, `MarkShipped` |
| Value objects for concepts | rules about money live in `Money`, not in every caller | `Money.Add` refuses mixed currencies |
| Time passed in | the domain stays deterministic and testable | `Place(address, now)` |
| Results for business refusals | the caller must handle "no" | `Result` with an `Error` code |
| Events for things that happened | other parts react without the aggregate knowing them | `Raise(new OrderPlaced(...))` |

[BoundaryTests.cs](../tests/Architecture.Tests/BoundaryTests.cs) fails the build when an entity
gets a public setter.

## What does not belong in the domain

- **Persistence.** No `DbContext`, no `SaveChanges`, no attributes from an ORM. The domain projects
  do not even reference EF Core.
- **The clock and randomness, implicitly.** `DateTime.Now` inside a method makes it untestable;
  receive `now` as a parameter. (Generating ids with `Guid.NewGuid()` in a factory is fine.)
- **Orchestration.** Loading, saving, calling other systems: that is the
  [application layer](layers.md).
- **Rules that need other aggregates' current state.** An aggregate decides on its own data. A rule
  across aggregates is either a sign the boundary is wrong, or a process coordinated by events. See
  [aggregates.md](aggregates.md).

## Domain services

When a rule genuinely belongs to no single entity — pricing that depends on a customer's contract
and a product's catalogue entry, say — put it in a *domain service*: a stateless class in the domain
layer, named in the ubiquitous language (`PriceCalculator`), that receives everything it needs as
arguments. Reach for it rarely; most "services" are anemic model in disguise.
