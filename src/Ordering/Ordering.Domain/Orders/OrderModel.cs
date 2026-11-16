using BuildingBlocks.Domain;

namespace Ordering.Domain.Orders;

public enum OrderStatus
{
    Draft,
    Placed,
    Shipped,
    Cancelled,
}

/// <summary>Something meaningful that happened to an order. Internal to Ordering: other contexts hear about it through integration events.</summary>
public sealed record OrderPlaced(OrderId OrderId, CustomerId CustomerId, Money Total, IReadOnlyList<OrderPlaced.Line> Lines, ShippingAddress ShippingAddress, DateTimeOffset OccurredAt) : IDomainEvent
{
    public sealed record Line(ProductCode Product, Quantity Quantity, Money UnitPrice);
}

public sealed record OrderCancelled(OrderId OrderId, string Reason, bool WasPlaced, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OrderShipped(OrderId OrderId, string TrackingCode, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// The business rules of an order that can say no, each with a stable code and a declared kind. The kind, not the
/// code, decides the HTTP status and what a message handler does with it.
/// </summary>
public static class OrderErrors
{
    public static readonly BusinessRuleError Empty = new("order.empty", "An order needs at least one line before it can be placed.");

    public static readonly BusinessRuleError TooManyLines = new("order.too-many-lines", $"An order has at most {Order.MaxLines} lines.");

    public static readonly BusinessRuleError AlreadyCancelled = new("order.already-cancelled", "The order is already cancelled.");

    public static readonly BusinessRuleError AlreadyShipped = new("order.already-shipped", "A shipped order can no longer be cancelled.");

    public static BusinessRuleError NotDraft(OrderStatus status) => new("order.not-draft", $"Only a draft order can be changed; this order is {status}.");

    public static BusinessRuleError NotPlaced(OrderStatus status) => new("order.not-placed", $"Only a placed order can be shipped; this order is {status}.");

    public static BusinessRuleError CurrencyMismatch(string orderCurrency, string priceCurrency) =>
        new("order.currency-mismatch", $"The order is in {orderCurrency}; a price in {priceCurrency} cannot be added.");

    public static BusinessRuleError PriceDiffers(ProductCode product) =>
        new("order.price-differs", $"{product} is already on the order at a different price.");

    public static BusinessRuleError QuantityTooLarge(ProductCode product) =>
        new("order.quantity-too-large", $"An order line holds at most {Quantity.Max} of {product}.");

    public static NotFoundError LineNotFound(ProductCode product) => new("order.line-not-found", $"{product} is not on the order.");
}

/// <summary>Loads and stores orders. The domain says what it needs; infrastructure decides how.</summary>
public interface IOrderRepository
{
    Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken);

    void Add(Order order);

    /// <summary>Commits the unit of work: domain events are dispatched and integration events stored in the same transaction.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
