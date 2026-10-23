# Pitfalls

Things that go wrong when applying these patterns, most of them learned the hard way.

## Modelling

### Aggregates that are too big

Symptoms: loading pulls hundreds of rows, unrelated changes cause concurrency conflicts. Usually the
cause is modelling the data instead of the invariants. Ask which rules must hold *immediately*; make
everything else a process between smaller aggregates. See [aggregates.md](aggregates.md).

### Anemic model with extra steps

Private setters plus a `SetStatus(OrderStatus status)` method is still an anemic model. Methods
should be business acts (`Place`, `Cancel`) that enforce rules, not setters with a different name.

### Exceptions for business rules

Throwing for "the order is already shipped" makes a normal outcome look like a crash, hides the
possible outcomes from the method signature, and tempts callers into `catch (Exception)`. Throw for
input that can never be valid; return a `Result` for rules. See [invariants.md](invariants.md).

### Leaking the domain into contracts

Putting `Money` or `OrderId` in an integration event couples every consumer to your domain project
and freezes your model. Contracts contain primitives and records of primitives only.

## Domain events

### Handlers that change other aggregates as a shortcut

A domain event handler that loads and changes another aggregate runs in the same transaction. That
is fine within one context and occasionally right, but it quietly breaks "one aggregate per
transaction": a conflict on the second aggregate now rolls back the first. Prefer an integration
event for anything that may be eventually consistent.

### Enqueueing to the outbox from the wrong place

Only enqueue from **domain event handlers**. The collector is scoped, and it is drained by the next
`SaveChangesAsync` of whichever DbContext in that scope saves first. A use case handler that
enqueues and then saves a different context sends the message through the wrong outbox.

### Cycles

Two handlers that raise events that trigger each other loop. `DbContextBase` stops after five rounds
with a clear exception; when you hit it, the design has a cycle.

## Outbox and messaging

### Expecting global order

The outbox keeps order **within a message group**, not across groups. Choose the group to match what
consumers need ordered — usually the aggregate id. A test in this sample was first written expecting
global order and failed; the code was right.

### Non-idempotent consumers

Delivery is at least once. A consumer that is not idempotent will one day double-ship. Use the inbox,
and prefer handlers that check current state anyway.

### Throwing on facts

A consumer receives "shipment dispatched" for an order it has cancelled. Throwing does not undo the
dispatch; it retries forever and blocks the group. Log the conflict, alert, and let a person or a
compensating process resolve it.

### Several publishers

Two instances publishing the same outbox break the order within a group. Use a distributed lock,
see [outbox-pattern.md](outbox-pattern.md#exactly-one-publisher-per-outbox).

### Forgetting to monitor

An outbox hides broker outages: the API stays green while messages pile up. Alert on the age of the
oldest unpublished message and on messages with many failed attempts.

## EF Core

### Get-only properties are not mapped

EF Core maps properties with a setter by convention. A value object like
`public int Quantity { get; }` is silently **not** mapped, and fails at run time with "No suitable
constructor was found" — or worse, loads with a default value. Map each property of an owned type
explicitly. The domain tests cannot catch this; the API tests did.

### Changes inside the aggregate do not bump the version

The aggregate's `Version` only protects you if a change to a line also counts as a change to the
order. That works because everything inside the aggregate is mapped as an **owned type**
(`OwnsOne`/`OwnsMany`) and `DbContextBase` walks from the changed owned entry up to the root. If you
map a child as a regular entity with its own `DbSet`, that link is gone.

### Synchronous `SaveChanges`

Domain event handlers are asynchronous. `DbContextBase.SaveChanges()` throws on purpose; always use
`SaveChangesAsync`.

### `DateTimeOffset` on SQLite

SQLite cannot compare or order `DateTimeOffset`. `DbContextBase` stores them as ISO 8601 strings on
SQLite, which sort chronologically only when every value has the same offset. Always use
`TimeProvider.GetUtcNow()`. On SQL Server or PostgreSQL this does not apply.

### `EnsureCreated` in production

The sample creates its schema on start because it is a demo. Use migrations or a database project
for anything real; `EnsureCreated` cannot evolve a schema.

## Process

### Big-bang adoption

Rewriting a system "the DDD way" in one go rarely survives contact with deadlines. Start with one
use case in one context, keep the old code working beside it, and move use cases over one by one.
See [applying-to-your-solution.md](applying-to-your-solution.md).

### Patterns without the language

Aggregates and outboxes without a [ubiquitous language](ubiquitous-language.md) produce a
technically tidy system that still does not match how the business thinks. The conversations with
domain experts are the point; the patterns support them.
