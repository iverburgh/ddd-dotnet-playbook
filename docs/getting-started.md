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
| 3. Wrong currency | a business rule says no: `422`, code `order.currency-mismatch`, as problem details | [HttpResults.cs](../src/Shop.Api/HttpResults.cs) |
| 4. Quantity 0 | a value object refuses to exist: `400`, code `invalid-input` | [ValueObjects.cs](../src/Ordering/Ordering.Domain/Orders/ValueObjects.cs) |
| 4a. Unreadable body | the request cannot be bound: `400`, code `malformed-request` | [ExceptionHandler.cs](../src/Shop.Api/ExceptionHandler.cs) |
| 4b. Missing line | the subject does not exist: `404`, code `order.line-not-found` | [Errors.cs](../src/BuildingBlocks/BuildingBlocks.Domain/Errors.cs) |
| 5. Place | `OrderPlaced` is raised, translated into `ordering.order-placed.v1` and stored in the outbox, in the same transaction as the order | [OrderEventHandlers.cs](../src/Ordering/Ordering.Application/OrderEventHandlers.cs) |
| 6. Shipments | the outbox publisher delivered the event; Shipping prepared a shipment | [ShippingUseCases.cs](../src/Shipping/Shipping.Application/ShippingUseCases.cs) |
| 7. Dispatch | Shipping raises `ShipmentDispatched` and publishes it the same way | same |
| 8. Order | Ordering consumed it: the order is `Shipped` and its `version` went up | [OrderEventHandlers.cs](../src/Ordering/Ordering.Application/OrderEventHandlers.cs) |
| 9. Cancel | too late: `422`, code `order.already-shipped` | [Order.cs](../src/Ordering/Ordering.Domain/Orders/Order.cs) |

Steps 6 and 8 cross a context boundary, so they are eventually consistent: if you are fast, the
other side may not have caught up yet. Send the request again.

Every refusal in steps 3, 4, 4a, 4b and 9 has the same problem details shape, with a `code` and a
`traceId`. See [error handling](error-handling.md).

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
- [OutboxAndInboxTests.cs](../tests/BuildingBlocks.Infrastructure.Tests/OutboxAndInboxTests.cs): ordering, backoff, group blocking, fairness, unresolvable and not-recorded messages, backlog metrics, deduplication.
- [BoundaryTests.cs](../tests/Architecture.Tests/BoundaryTests.cs): the layer and context rules.
- [ContractTests.cs](../tests/Architecture.Tests/ContractTests.cs): every integration event survives the trip through the outbox serializer.
- [OrderFlowTests.cs](../tests/Shop.Api.Tests/OrderFlowTests.cs): the flow above, automated.
- [HandlerErrorTests.cs](../tests/Ordering.Application.Tests/HandlerErrorTests.cs): handlers return typed failures and save nothing.
- [ProblemDetailsMappingTests.cs](../tests/Shop.Api.Tests/ProblemDetailsMappingTests.cs) and [ErrorHandlingFlowTests.cs](../tests/Shop.Api.Tests/ErrorHandlingFlowTests.cs): every error kind to status and problem details; unexpected exceptions become a `500` that leaks nothing.
- [ErrorCatalogueTests.cs](../tests/Architecture.Tests/ErrorCatalogueTests.cs): the error codes are an approved, unique catalogue.

Next: [apply it to your own solution](applying-to-your-solution.md).
