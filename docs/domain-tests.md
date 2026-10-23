# Domain tests without a database or web server

When the business rules live in the domain, and the domain depends on nothing, the rules can be
tested as plain objects: construct, act, assert. No database, no web server, no mocks, no setup.
The 45 domain tests in this sample run in well under a second.

## The project setup is the guarantee

[Ordering.Domain.Tests.csproj](../tests/Ordering.Domain.Tests/Ordering.Domain.Tests.csproj)
references exactly one project: `Ordering.Domain`. It cannot reach a DbContext even by accident.
If a rule cannot be tested this way, the rule is in the wrong layer.

## What a domain test looks like

From [OrderTests.cs](../tests/Ordering.Domain.Tests/OrderTests.cs):

```csharp
[Test]
public void A_shipped_order_cannot_be_cancelled()
{
    var order = PlacedOrder();
    order.MarkShipped("TRACK12345NL", Now);

    var result = order.Cancel("Too late", Now);

    Assert.That(result.Error, Is.EqualTo(OrderErrors.AlreadyShipped));
    Assert.That(order.Status, Is.EqualTo(OrderStatus.Shipped));
}
```

- **The name is a business rule** in the [ubiquitous language](ubiquitous-language.md). The list
  of test names reads as the specification of an order.
- **Time is a constant.** Methods take `now`, so tests are deterministic.
- **Small helpers build the state** (`DraftOrder()`, `PlacedOrder()`) by calling real domain
  methods, never by setting fields. If the domain cannot reach a state, a test should not either.

## What to test

| Test | Example |
|---|---|
| Each business rule, happy and refused | `An_order_has_at_most_twenty_lines` |
| A refusal leaves no trace | `An_empty_order_cannot_be_placed_and_raises_nothing` |
| The events raised, with their content | `Placing_an_order_raises_OrderPlaced_with_what_was_ordered` |
| Value object validation, as table tests | `[TestCase(0)] [TestCase(-1)] Quantity_is_between_one_and_the_maximum` |
| Value semantics | `Adding_money_returns_a_new_value_and_leaves_the_original_alone` |
| Entity identity | `Two_orders_are_the_same_entity_only_when_their_ids_match` |

Assert on the error **code** or the `Error` instance, not on the message text: messages are for
people and change.

## The rest of the test pyramid

Domain tests cover the rules. Other things still need testing — once, at the right level:

| Concern | Test project | Uses |
|---|---|---|
| Events dispatched before commit, outbox atomicity, versioning, retries, inbox | [BuildingBlocks.Infrastructure.Tests](../tests/BuildingBlocks.Infrastructure.Tests) | real EF Core on in-memory SQLite, `FakeTimeProvider` |
| Layer and context boundaries | [Architecture.Tests](../tests/Architecture.Tests) | reflection on compiled assemblies |
| Mapping, wiring, HTTP, the flow across contexts | [Shop.Api.Tests](../tests/Shop.Api.Tests) | `WebApplicationFactory`, SQLite files |

Note what is missing: unit tests of application handlers with mocked repositories. A handler is
three lines of orchestration; mocking its collaborators tests the mocks. The API tests cover the
handlers for real, and caught a mapping bug during the making of this sample that a mocked test
never would have (see [pitfalls.md](pitfalls.md#get-only-properties-are-not-mapped)).

## Testing eventual consistency

Assertions about another context poll until a deadline instead of sleeping a fixed time:

```csharp
var order = await EventuallyAsync(
    () => GetJsonAsync($"/orders/{orderId}"),
    o => o.GetProperty("status").GetString() == "Shipped");
```

Fast when the system is fast, patient when CI is slow, and a clear failure message when the
condition never comes.
