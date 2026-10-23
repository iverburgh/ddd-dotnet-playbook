# Bounded contexts

A bounded context is the boundary within which one model, and one [ubiquitous language](ubiquitous-language.md),
applies. Inside, every term has exactly one meaning. Across the boundary, models are translated
explicitly.

## In the sample

| | Ordering | Shipping |
|---|---|---|
| Owns | orders, lines, prices, currency | shipments, items, delivery address, tracking |
| Knows of the other | `ShipmentDispatchedIntegrationEvent` | `OrderPlacedIntegrationEvent`, `OrderCancelledIntegrationEvent` |
| Database tables | `Orders`, `OrderLines`, `OrderingOutboxMessages`, `OrderingInboxMessages` | `Shipments`, `ShipmentItems`, `ShippingOutboxMessages`, `ShippingInboxMessages` |
| DbContext | `OrderingDbContext` | `ShippingDbContext` |

Each context is four projects:

```text
Ordering.Domain          the model
Ordering.Application     the use cases
Ordering.Infrastructure  persistence and wiring
Ordering.Contracts       the only thing other contexts may reference
```

## The rules that make a boundary real

1. **A context never references another context's Domain, Application or Infrastructure.** Only its
   Contracts. [BoundaryTests.cs](../tests/Architecture.Tests/BoundaryTests.cs) fails the build if
   this is broken.
2. **A context owns its data.** Shipping does not read the `Orders` table, even though in this
   sample both databases are SQLite files next to each other. If Shipping needs order data, Ordering
   publishes it.
3. **Contracts are a public API.** They are versioned by name (`ordering.order-placed.v1`), contain
   only primitives and nested records, and carry only what consumers need. `OrderPlacedIntegrationEvent`
   has no prices, because Shipping has no use for them.
4. **Each context models what it needs.** Shipping's `ShipmentItem` is a value object with a product
   code and a quantity; Ordering's `OrderLine` is an entity with a price. Neither is "the" product
   model.

## Communication between contexts

Contexts talk asynchronously through integration events, published via the
[outbox](outbox-pattern.md). That gives:

- **Autonomy**: Ordering accepts orders while Shipping is down; Shipping catches up later.
- **Explicit translation**: the domain event (`OrderPlaced`, rich types) is mapped to a contract
  (`OrderPlacedIntegrationEvent`, primitives) in one place: [OrderEventHandlers.cs](../src/Ordering/Ordering.Application/OrderEventHandlers.cs).
- **Eventual consistency**: after placing an order, there is a moment where no shipment exists yet.
  Design the UI and the business process for that moment.

When a consumer receives a fact it cannot accept — Shipping already dispatched an order that
Ordering has cancelled — it must not throw (a retry will not change the past). It records the
conflict for a person or a follow-up process. See `CancelShipmentWhenOrderCancelled` in
[ShippingUseCases.cs](../src/Shipping/Shipping.Application/ShippingUseCases.cs).

## Finding the boundaries in your own domain

- **Follow the language.** Where the same word starts to mean something else, there is a boundary.
- **Follow the teams.** A context that two teams change all the time is usually two contexts.
- **Follow the rules.** Things that must be consistent together belong together; things that may be
  eventually consistent can be apart.
- **Start bigger than you think.** Splitting a context later is easier than merging two that grew
  apart. A modular monolith like this sample lets you move a boundary without redeploying the world.

## One process, several contexts

This sample hosts both contexts in one API (a modular monolith). Nothing in the design depends on
that: replace the in-memory bus with a broker, give each context its own host, and the code of the
contexts does not change. See [outbox-pattern.md](outbox-pattern.md#replacing-the-in-memory-bus).
