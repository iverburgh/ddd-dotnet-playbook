# Ubiquitous language

The words the business uses are the words in the code. Not roughly, not translated: the same words,
with the same meaning, in conversations, tickets, tests, class names and method names.

## Why it matters

When a domain expert says "an order is placed" and the code says `order.Status = 2; repo.Update(order)`,
every conversation needs a translator, and translations drift. When the code says
`order.Place(address, now)` and raises `OrderPlaced`, a domain expert can read the test and tell you
whether it is right.

## What it looks like in the sample

| The business says | The code says |
|---|---|
| A customer starts an order | `Order.Start(...)` |
| You can only change a draft | `OrderErrors.NotDraft`, code `order.not-draft` |
| The order is placed | `Order.Place(...)`, event `OrderPlaced` |
| A shipped order cannot be cancelled | `OrderErrors.AlreadyShipped` |
| The parcel is dispatched | `Shipment.Dispatch(...)`, event `ShipmentDispatched` |

The tests use the same language as sentences:
`A_shipped_order_cannot_be_cancelled`, `Cancelling_twice_is_harmless_and_raises_one_event`.

## The language belongs to a context

The same word can mean different things in different parts of the business. Ordering talks about a
*shipping address*; Shipping talks about a *delivery address*. Ordering's *order* has lines and
prices; Shipping only needs an `OrderReference`. That is not inconsistency: each
[bounded context](bounded-contexts.md) has its own language, and the boundary is where translation
happens explicitly — in the integration event contracts.

## How to apply it

1. **Write the glossary with the experts**, per context. A table of term, meaning and example is
   enough. Keep it next to the code.
2. **Name by intent, not by data.** `Place`, `Cancel`, `MarkShipped` instead of `Update`,
   `SetStatus`, `Save`. If you cannot find a business word for a method, ask whether the method
   should exist.
3. **Name error codes and events in the language too.** `order.already-shipped` is something a
   support desk can understand; `ERR_042` is not.
4. **Rename when the language changes.** When the experts start saying "reserve" instead of "hold",
   rename the code. Stale names are how a model rots.
5. **Listen for friction.** If people keep explaining that "in the system, X actually means Y", the
   model is wrong, not the people.

## Smells

- Words like `Manager`, `Processor`, `Helper`, `Data`, `Info` in the domain layer.
- Business rules hidden behind generic verbs: `UpdateOrder(OrderDto dto)`.
- Two words for one concept in the same context, or one word for two concepts.
- Technical status numbers in conversations with the business.
