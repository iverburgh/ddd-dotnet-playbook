using BuildingBlocks.Domain;
using Ordering.Domain.Orders;

namespace Ordering.Application.Tests;

/// <summary>
/// Expected failures come back as typed results, unsaved; only input that can never be valid and bugs throw.
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class HandlerErrorTests
{
    private readonly FakeOrderRepository _orders = new();

    [Test]
    public async Task An_unknown_order_is_a_not_found_result_not_an_exception()
    {
        var orderId = Guid.NewGuid();

        var result = await new RemoveOrderLineHandler(_orders).Handle(new RemoveOrderLine(orderId, "BOOK-1"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.TypeOf<NotFoundError>());
            Assert.That(result.Error, Is.EqualTo(OrderingErrors.OrderNotFound(orderId)));
            Assert.That(_orders.Saves, Is.Zero);
        });
    }

    [Test]
    public async Task A_business_rule_that_says_no_is_returned_and_nothing_is_saved()
    {
        var order = _orders.AddDraft("EUR");

        var result = await new AddOrderLineHandler(_orders).Handle(new AddOrderLine(order.Id.Value, "PEN-BLUE", 1, 2.00m, "USD"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.TypeOf<BusinessRuleError>());
            Assert.That(result.Error?.Code, Is.EqualTo("order.currency-mismatch"));
            Assert.That(_orders.Saves, Is.Zero);
        });
    }

    [Test]
    public async Task A_missing_line_is_not_found_rather_than_a_broken_rule()
    {
        var order = _orders.AddDraft("EUR");

        var result = await new RemoveOrderLineHandler(_orders).Handle(new RemoveOrderLine(order.Id.Value, "BOOK-1"), CancellationToken.None);

        Assert.That(result.Error, Is.TypeOf<NotFoundError>().With.Property(nameof(Error.Code)).EqualTo("order.line-not-found"));
    }

    [Test]
    public async Task A_success_is_saved()
    {
        var order = _orders.AddDraft("EUR");

        var result = await new AddOrderLineHandler(_orders).Handle(new AddOrderLine(order.Id.Value, "PEN-BLUE", 1, 2.00m, "EUR"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_orders.Saves, Is.EqualTo(1));
        });
    }

    [Test]
    public void Input_that_can_never_be_valid_throws_and_is_not_a_result()
    {
        var order = _orders.AddDraft("EUR");

        Assert.That(
            () => new AddOrderLineHandler(_orders).Handle(new AddOrderLine(order.Id.Value, "PEN-BLUE", 0, 2.00m, "EUR"), CancellationToken.None),
            Throws.TypeOf<InvariantViolationException>());
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        private readonly Dictionary<OrderId, Order> _orders = [];

        public int Saves { get; private set; }

        public Order AddDraft(string currency)
        {
            var order = Order.Start(OrderId.New(), new CustomerId(Guid.NewGuid()), currency, DateTimeOffset.UtcNow);
            _orders.Add(order.Id, order);
            return order;
        }

        public Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken) => Task.FromResult(_orders.GetValueOrDefault(id));

        public void Add(Order order) => _orders.Add(order.Id, order);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }
}
