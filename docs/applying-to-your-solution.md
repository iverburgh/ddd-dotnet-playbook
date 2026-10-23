# Applying it to your own solution

A step-by-step path from an existing .NET solution to the structure in this playbook. Each step is
useful on its own; stop wherever the value runs out.

## 0. Before you start

- **Pick one bounded context and one use case** with real business rules — something people argue
  about. Not CRUD: a rich model adds nothing to "save these fields".
- **Talk to the domain experts.** Write down the terms and the rules of that use case. This is your
  first [ubiquitous language](ubiquitous-language.md) glossary and the test list for step 3.

## 1. Copy the building blocks

Copy [src/BuildingBlocks](../src/BuildingBlocks) into your solution and rename the namespace if you
like. They are small on purpose; you own them now.

| Project | What you get | Depends on |
|---|---|---|
| `BuildingBlocks.Domain` | `Entity<TId>`, `AggregateRoot<TId>`, `IDomainEvent`, `Invariant`, `Result`, `Error` | nothing |
| `BuildingBlocks.Contracts` | `IIntegrationEvent`, `[IntegrationEventName]` | nothing |
| `BuildingBlocks.Application` | `IDomainEventHandler<T>`, `IIntegrationEventHandler<T>`, `IOutbox` | Domain, Contracts |
| `BuildingBlocks.Infrastructure` | `DbContextBase`, outbox, inbox, dispatcher, DI extensions | EF Core Relational |

If you use SQL Server or PostgreSQL, nothing changes: `DbContextBase` is provider-neutral.

## 2. Create the projects for your context

```text
src/Billing/
  Billing.Domain/          -> BuildingBlocks.Domain
  Billing.Application/     -> Billing.Domain, Billing.Contracts, BuildingBlocks.Application
  Billing.Infrastructure/  -> Billing.Application, BuildingBlocks.Infrastructure
  Billing.Contracts/       -> BuildingBlocks.Contracts
tests/
  Billing.Domain.Tests/    -> Billing.Domain (only!)
```

Copy [Architecture.Tests](../tests/Architecture.Tests) and add your assemblies to the list, so the
boundaries hold from day one.

## 3. Model the aggregate, test first

Turn the rules from step 0 into domain tests, then make them pass. Use
[Order.cs](../src/Ordering/Ordering.Domain/Orders/Order.cs) as the template:

- [ ] Derive the root from `AggregateRoot<TId>` with a strongly typed id.
- [ ] Private setters; read-only collections over private lists.
- [ ] A named factory method (`Invoice.Issue(...)`) and a private parameterless constructor for EF.
- [ ] Value objects for every concept with rules (`Money`, `InvoiceNumber`), validated with
      `Invariant.Require`.
- [ ] One method per business act, returning `Result`; error codes in one `XxxErrors` class.
- [ ] `now` as a parameter, never `DateTime.Now`.
- [ ] `Raise(new SomethingHappened(...))` after each successful state change that others care about.
- [ ] A repository interface in the domain: `GetAsync`, `Add`, `SaveChangesAsync`.

Guides: [rich domain model](rich-domain-model.md), [entities and value objects](entities-and-value-objects.md),
[aggregates](aggregates.md), [invariants](invariants.md), [domain tests](domain-tests.md).

## 4. Persist it

Create a DbContext deriving from `DbContextBase` (template:
[OrderingDbContext.cs](../src/Ordering/Ordering.Infrastructure/OrderingDbContext.cs)):

```csharp
public sealed class BillingDbContext(
    DbContextOptions<BillingDbContext> options,
    IDomainEventDispatcher dispatcher,
    OutboxCollector outbox,
    OutboxSignal signal) : DbContextBase(options, dispatcher, outbox, signal)
{
    protected override string TablePrefix => "Billing";

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new InvoiceConfiguration());
}
```

- [ ] Map ids and single-value objects with `HasConversion`.
- [ ] Map **everything inside the aggregate** as owned types (`OwnsOne`, `OwnsMany`).
- [ ] Map every get-only property explicitly.
- [ ] Add the tables to your schema: the aggregate's tables, plus `BillingOutboxMessages` and
      `BillingInboxMessages`. Generate the script once with `dotnet ef dbcontext script` or
      `context.Database.GenerateCreateScript()` and put it in your migrations or database project.
- [ ] Implement the repository (template: [OrderRepository.cs](../src/Ordering/Ordering.Infrastructure/OrderRepository.cs)).

### Already have a DbContext?

You can adopt `DbContextBase` on an existing context: derive from it, move `OnModelCreating` into
`ConfigureModel`, and add the two tables. Existing entities keep working; only aggregate roots get
event dispatch and versioning. If you cannot change the base class, port the `SaveChangesAsync`
override and the model configuration into your own context — they are self-contained.

The `Version` column on existing tables needs a default of `0`.

## 5. Write the use cases

One handler per use case in the application layer (template:
[OrderCommands.cs](../src/Ordering/Ordering.Application/OrderCommands.cs)): load, call one method,
save if it succeeded, return the `Result`. Map results to HTTP once, by kind (template:
[HttpResults.cs](../src/Shop.Api/HttpResults.cs)): invariant violation `400`, business rule `422`,
`*.not-found` `404`.

The old code path can stay alongside. Move callers over one at a time.

## 6. Publish integration events

When another context or system must learn about a change:

- [ ] Define the contract in `Billing.Contracts`: a record with primitives,
      `[IntegrationEventName("billing.invoice-issued.v1")]`.
- [ ] Write a domain event handler that maps the domain event to the contract and calls
      `outbox.Enqueue(contract, messageGroup: $"invoice-{id}")`.
- [ ] Implement `IMessageBus` over your broker, see
      [outbox-pattern.md](outbox-pattern.md#replacing-the-in-memory-bus).
- [ ] With more than one instance: add a distributed lock to the publisher.
- [ ] Add an alert on the oldest unpublished outbox message.

## 7. Consume integration events

- [ ] Reference only the other context's `Contracts` project.
- [ ] Implement `IIntegrationEventHandler<TEvent>`; make it naturally idempotent where cheap.
- [ ] Register it with `AddIntegrationEventHandler<TEvent, THandler, TDbContext>()` so the inbox
      deduplicates it.
- [ ] Decide what happens when the fact conflicts with your state. Log and alert; do not throw.

## 8. Wire the module

One extension method per context, called from the host (template:
[OrderingModule.cs](../src/Ordering/Ordering.Infrastructure/OrderingModule.cs)):

```csharp
builder.Services.AddBillingModule(db => db.UseSqlServer(builder.Configuration.GetConnectionString("Billing")));
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
```

## Checklist for review

- [ ] The domain project references only `BuildingBlocks.Domain`.
- [ ] Every rule from the glossary has a domain test with its name in the business language.
- [ ] No public setters on entities; no `IQueryable` from repositories.
- [ ] One aggregate changed per transaction.
- [ ] Integration events are contracts with primitives, versioned by name.
- [ ] Consumers are idempotent; conflicts are logged, not thrown.
- [ ] Architecture tests cover the new assemblies.

Then read the [pitfalls](pitfalls.md).
