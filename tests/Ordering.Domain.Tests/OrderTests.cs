using BuildingBlocks.Domain;
using Ordering.Domain.Orders;

namespace Ordering.Domain.Tests;

public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2025, 3, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly ShippingAddress Address = new("Main Street 1", "1234 AB", "Springfield", "NL");
    private static readonly ProductCode Book = new("BOOK-1984");
    private static readonly ProductCode Pen = new("PEN-BLUE");

    private static Order DraftOrder() => Order.Start(OrderId.New(), new CustomerId(Guid.NewGuid()), "EUR", Now);

    private static Money Eur(decimal amount) => new(amount, "EUR");

    private static Order PlacedOrder()
    {
        var order = DraftOrder();
        order.AddLine(Book, new Quantity(2), Eur(12.50m));
        order.Place(Address, Now);
        order.ClearDomainEvents();
        return order;
    }

    [Test]
    public void A_new_order_is_a_draft_without_lines_or_events()
    {
        var order = DraftOrder();

        Assert.Multiple(() =>
        {
            Assert.That(order.Status, Is.EqualTo(OrderStatus.Draft));
            Assert.That(order.Lines, Is.Empty);
            Assert.That(order.Total, Is.EqualTo(Eur(0)));
            Assert.That(order.DomainEvents, Is.Empty);
        });
    }

    [Test]
    public void An_order_cannot_start_in_an_invalid_currency() =>
        Assert.Throws<InvariantViolationException>(() => Order.Start(OrderId.New(), new CustomerId(Guid.NewGuid()), "euro", Now));

    [Test]
    public void Adding_the_same_product_twice_merges_the_quantities()
    {
        var order = DraftOrder();

        order.AddLine(Book, new Quantity(2), Eur(12.50m));
        order.AddLine(Book, new Quantity(3), Eur(12.50m));

        Assert.That(order.Lines.Single().Quantity, Is.EqualTo(new Quantity(5)));
        Assert.That(order.Total, Is.EqualTo(Eur(62.50m)));
    }

    [Test]
    public void The_total_adds_up_all_lines()
    {
        var order = DraftOrder();

        order.AddLine(Book, new Quantity(2), Eur(12.50m));
        order.AddLine(Pen, new Quantity(4), Eur(1.25m));

        Assert.That(order.Total, Is.EqualTo(Eur(30.00m)));
    }

    [Test]
    public void A_price_in_another_currency_is_refused()
    {
        var order = DraftOrder();

        var result = order.AddLine(Book, new Quantity(1), new Money(10m, "USD"));

        Assert.That(result.Error?.Code, Is.EqualTo("order.currency-mismatch"));
        Assert.That(order.Lines, Is.Empty);
    }

    [Test]
    public void The_same_product_at_a_different_price_is_refused()
    {
        var order = DraftOrder();
        order.AddLine(Book, new Quantity(1), Eur(12.50m));

        var result = order.AddLine(Book, new Quantity(1), Eur(9.99m));

        Assert.That(result.Error?.Code, Is.EqualTo("order.price-differs"));
    }

    [Test]
    public void A_line_cannot_grow_beyond_the_maximum_quantity()
    {
        var order = DraftOrder();
        order.AddLine(Book, new Quantity(Quantity.Max), Eur(1m));

        var result = order.AddLine(Book, new Quantity(1), Eur(1m));

        Assert.That(result.Error?.Code, Is.EqualTo("order.quantity-too-large"));
        Assert.That(order.Lines.Single().Quantity.Value, Is.EqualTo(Quantity.Max));
    }

    [Test]
    public void An_order_has_at_most_twenty_lines()
    {
        var order = DraftOrder();
        for (var i = 0; i < Order.MaxLines; i++)
        {
            Assert.That(order.AddLine(new ProductCode($"P-{i}"), new Quantity(1), Eur(1m)).IsSuccess, Is.True);
        }

        var result = order.AddLine(new ProductCode("ONE-TOO-MANY"), new Quantity(1), Eur(1m));

        Assert.That(result.Error, Is.EqualTo(OrderErrors.TooManyLines));
        Assert.That(order.Lines, Has.Count.EqualTo(Order.MaxLines));
    }

    [Test]
    public void Removing_a_product_that_is_not_on_the_order_is_refused() =>
        Assert.That(DraftOrder().RemoveLine(Book).Error?.Code, Is.EqualTo("order.line-not-found"));

    [Test]
    public void An_empty_order_cannot_be_placed_and_raises_nothing()
    {
        var order = DraftOrder();

        var result = order.Place(Address, Now);

        Assert.That(result.Error, Is.EqualTo(OrderErrors.Empty));
        Assert.That(order.Status, Is.EqualTo(OrderStatus.Draft));
        Assert.That(order.DomainEvents, Is.Empty);
    }

    [Test]
    public void Placing_an_order_raises_OrderPlaced_with_what_was_ordered()
    {
        var order = DraftOrder();
        order.AddLine(Book, new Quantity(2), Eur(12.50m));

        var result = order.Place(Address, Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(order.Status, Is.EqualTo(OrderStatus.Placed));
        var placed = order.DomainEvents.OfType<OrderPlaced>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(placed.OrderId, Is.EqualTo(order.Id));
            Assert.That(placed.Total, Is.EqualTo(Eur(25.00m)));
            Assert.That(placed.Lines.Single(), Is.EqualTo(new OrderPlaced.Line(Book, new Quantity(2), Eur(12.50m))));
            Assert.That(placed.ShippingAddress, Is.EqualTo(Address));
            Assert.That(placed.OccurredAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public void A_placed_order_can_no_longer_change()
    {
        var order = PlacedOrder();

        Assert.Multiple(() =>
        {
            Assert.That(order.AddLine(Pen, new Quantity(1), Eur(1m)).Error?.Code, Is.EqualTo("order.not-draft"));
            Assert.That(order.RemoveLine(Book).Error?.Code, Is.EqualTo("order.not-draft"));
            Assert.That(order.Place(Address, Now).Error?.Code, Is.EqualTo("order.not-draft"));
        });
    }

    [Test]
    public void Cancelling_a_placed_order_tells_that_it_had_been_placed()
    {
        var order = PlacedOrder();

        order.Cancel("Changed my mind", Now);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Cancelled));
        Assert.That(order.DomainEvents.OfType<OrderCancelled>().Single().WasPlaced, Is.True);
    }

    [Test]
    public void Cancelling_a_draft_tells_that_it_was_never_placed()
    {
        var order = DraftOrder();

        order.Cancel("Abandoned cart", Now);

        Assert.That(order.DomainEvents.OfType<OrderCancelled>().Single().WasPlaced, Is.False);
    }

    [Test]
    public void An_order_is_cancelled_only_once()
    {
        var order = PlacedOrder();
        order.Cancel("First", Now);
        order.ClearDomainEvents();

        var result = order.Cancel("Second", Now);

        Assert.That(result.Error, Is.EqualTo(OrderErrors.AlreadyCancelled));
        Assert.That(order.DomainEvents, Is.Empty);
    }

    [Test]
    public void A_shipped_order_cannot_be_cancelled()
    {
        var order = PlacedOrder();
        order.MarkShipped("TRACK12345NL", Now);

        var result = order.Cancel("Too late", Now);

        Assert.That(result.Error, Is.EqualTo(OrderErrors.AlreadyShipped));
        Assert.That(order.Status, Is.EqualTo(OrderStatus.Shipped));
    }

    [Test]
    public void A_cancellation_needs_a_reason() =>
        Assert.Throws<InvariantViolationException>(() => PlacedOrder().Cancel("  ", Now));

    [Test]
    public void Only_a_placed_order_can_be_shipped()
    {
        var order = DraftOrder();

        var result = order.MarkShipped("TRACK12345NL", Now);

        Assert.That(result.Error?.Code, Is.EqualTo("order.not-placed"));
    }

    [Test]
    public void Shipping_raises_OrderShipped()
    {
        var order = PlacedOrder();

        order.MarkShipped("TRACK12345NL", Now);

        Assert.That(order.DomainEvents.Single(), Is.EqualTo(new OrderShipped(order.Id, "TRACK12345NL", Now)));
    }

    [Test]
    public void Two_orders_are_the_same_entity_only_when_their_ids_match()
    {
        var id = OrderId.New();
        var first = Order.Start(id, new CustomerId(Guid.NewGuid()), "EUR", Now);
        var sameId = Order.Start(id, new CustomerId(Guid.NewGuid()), "USD", Now);

        Assert.That(first, Is.EqualTo(sameId));
        Assert.That(first, Is.Not.EqualTo(DraftOrder()));
    }
}
