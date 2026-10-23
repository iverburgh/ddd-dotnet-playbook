# Getting started

A guided tour of the sample in about fifteen minutes.

## Run it

```shell
dotnet test Shop.slnx
dotnet run --project src/Shop.Api
```

The API listens on `http://localhost:5080` and creates `ordering.db` and `shipping.db` (SQLite) in
the project folder. Delete them to start over.

## Walk the flow

Open [Shop.Api.http](../src/Shop.Api/Shop.Api.http) in Visual Studio, Rider or VS Code with the
REST Client extension, and send the requests in order.

| Step | What happens | Where |
|---|---|---|
| 1. Start an order | `Order.Start` creates a draft | [Order.cs](../src/Ordering/Ordering.Domain/Orders/Order.cs) |
| 2. Add a line | `Order.AddLine` checks currency, line count, quantity | same |
| 3. Wrong currency | a business rule says no: `422`, code `order.currency-mismatch` | [HttpResults.cs](../src/Shop.Api/HttpResults.cs) |
| 4. Quantity 0 | a value object refuses to exist: `400` | [ValueObjects.cs](../src/Ordering/Ordering.Domain/Orders/ValueObjects.cs) |
| 5. Place | `OrderPlaced` is raised, translated into `ordering.order-placed.v1` and stored in the outbox, in the same transaction as the order | [OrderEventHandlers.cs](../src/Ordering/Ordering.Application/OrderEventHandlers.cs) |
| 6. Shipments | the outbox publisher delivered the event; Shipping prepared a shipment | [ShippingUseCases.cs](../src/Shipping/Shipping.Application/ShippingUseCases.cs) |
| 7. Dispatch | Shipping raises `ShipmentDispatched` and publishes it the same way | same |
| 8. Order | Ordering consumed it: the order is `Shipped` and its `version` went up | [OrderEventHandlers.cs](../src/Ordering/Ordering.Application/OrderEventHandlers.cs) |
| 9. Cancel | too late: `422`, code `order.already-shipped` | [Order.cs](../src/Ordering/Ordering.Domain/Orders/Order.cs) |

Steps 6 and 8 cross a context boundary, so they are eventually consistent: if you are fast, the
other side may not have caught up yet. Send the request again.

## Look inside the databases

Each context has its own database with its own outbox and inbox tables:

```sql
-- ordering.db
SELECT Id, Type, MessageGroup, PublishedAt, FailedAttempts FROM OrderingOutboxMessages;
SELECT * FROM OrderingInboxMessages;
```

## Read the tests

The tests are the most compact description of the behaviour:

- [OrderTests.cs](../tests/Ordering.Domain.Tests/OrderTests.cs): every business rule of an order, in milliseconds.
- [SaveChangesTests.cs](../tests/BuildingBlocks.Infrastructure.Tests/SaveChangesTests.cs): events dispatched before commit, outbox atomicity, versioning.
- [OutboxAndInboxTests.cs](../tests/BuildingBlocks.Infrastructure.Tests/OutboxAndInboxTests.cs): ordering, backoff, group blocking, deduplication.
- [BoundaryTests.cs](../tests/Architecture.Tests/BoundaryTests.cs): the layer and context rules.
- [OrderFlowTests.cs](../tests/Shop.Api.Tests/OrderFlowTests.cs): the flow above, automated.

Next: [apply it to your own solution](applying-to-your-solution.md).
