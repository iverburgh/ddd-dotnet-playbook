# The repository pattern

A repository gives the domain the illusion of an in-memory collection of aggregates: get one by id,
add a new one, save. How that happens — SQL, EF Core, a document store — is hidden.

## In the sample

The interface is in the **domain**, written in its terms
([OrderModel.cs](../src/Ordering/Ordering.Domain/Orders/OrderModel.cs)):

```csharp
public interface IOrderRepository
{
    Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken);
    void Add(Order order);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

The implementation is in **infrastructure**
([OrderRepository.cs](../src/Ordering/Ordering.Infrastructure/OrderRepository.cs)):

```csharp
internal sealed class OrderRepository(OrderingDbContext db) : IOrderRepository
{
    public Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken) =>
        db.Orders.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public void Add(Order order) => db.Orders.Add(order);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
```

The dependency points inward: infrastructure depends on the domain, never the other way round.

## Rules

1. **One repository per aggregate root.** There is an `IOrderRepository`; there is no
   `IOrderLineRepository`. Lines are reached through the order.
2. **Return whole aggregates.** Owned types are loaded with their owner automatically, so `GetAsync`
   always returns an order with all its lines.
3. **A small, intention-revealing interface.** `GetAsync`, `Add`, and lookups the domain actually
   needs (`FindByOrderAsync` in Shipping). No `IQueryable`, no generic `Find(Expression<...>)`:
   those leak persistence into every caller and make the aggregate boundary meaningless.
4. **Saving belongs to the repository.** In this design one use case changes one aggregate, so the
   repository of that aggregate commits it. A separate `IUnitOfWork` only earns its place when one
   transaction legitimately spans several repositories — which the [aggregate rules](aggregates.md)
   avoid.
5. **No update method.** The DbContext tracks changes to loaded aggregates; `SaveChangesAsync` is
   enough.
6. **No generic `IRepository<T>` base.** Every aggregate needs different queries, and a generic
   base offers operations (delete, list everything) that most aggregates must not support.

## Reads do not need repositories

Repositories serve the write side: load an aggregate to change it. Screens and APIs that only show
data use a query service that returns read models shaped for the caller:

```csharp
public interface IOrderQueries
{
    Task<OrderDetails?> GetAsync(Guid orderId, CancellationToken cancellationToken);
}
```

[OrderRepository.cs](../src/Ordering/Ordering.Infrastructure/OrderRepository.cs) implements it with
`AsNoTracking()`. For heavier reads, project straight to the read model in SQL (`Select(o => new
OrderDetails(...))`) or use a separate read store. This split is the essence of CQRS, without
needing any framework.

## Testing

Domain tests do not need repositories at all; they work on aggregates directly. The repository is
covered by the API tests, which run the real thing against SQLite. Avoid mocking a repository just
to test that a handler calls `SaveChangesAsync`: such tests break on every refactoring and prove
little. See [domain-tests.md](domain-tests.md).
