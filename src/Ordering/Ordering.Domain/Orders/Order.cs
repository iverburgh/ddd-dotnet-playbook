using BuildingBlocks.Domain;

namespace Ordering.Domain.Orders;

/// <summary>
/// A customer's order: the aggregate root of the Ordering context. Every change goes through its
/// methods, which enforce the rules below, so an order can never exist in a state the business
/// would not recognise.
/// <list type="bullet">
/// <item>Only a draft can change; a placed order is a commitment.</item>
/// <item>All prices on an order are in the order's currency.</item>
/// <item>An order has at most <see cref="MaxLines"/> lines, one per product.</item>
/// <item>An order is placed with at least one line, and cancelled or shipped only once.</item>
/// </list>
/// </summary>
public sealed class Order : AggregateRoot<OrderId>
{
    public const int MaxLines = 20;

    private readonly List<OrderLine> _lines = [];

    private Order(OrderId id, CustomerId customerId, string currency, DateTimeOffset createdAt)
        : base(id)
    {
        CustomerId = customerId;
        Currency = currency;
        Status = OrderStatus.Draft;
        CreatedAt = createdAt;
    }

    // For the ORM only.
    private Order()
    {
        CustomerId = null!;
        Currency = string.Empty;
    }

    public CustomerId CustomerId { get; private set; }

    public string Currency { get; private set; }

    public OrderStatus Status { get; private set; }

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    public ShippingAddress? ShippingAddress { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PlacedAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public string? TrackingCode { get; private set; }

    public Money Total => _lines.Aggregate(Money.Zero(Currency), (total, line) => total.Add(line.LineTotal));

    /// <summary>Starts a draft order for a customer, priced in one currency.</summary>
    public static Order Start(OrderId id, CustomerId customerId, string currency, DateTimeOffset now)
    {
        Invariant.Require(Money.IsValidCurrency(currency), $"A currency is a three-letter ISO 4217 code such as EUR, not '{currency}'.");
        return new Order(id, customerId, currency, now);
    }

    public Result AddLine(ProductCode product, Quantity quantity, Money unitPrice)
    {
        if (Status != OrderStatus.Draft)
        {
            return Result.Failure(OrderErrors.NotDraft(Status));
        }

        if (unitPrice.Currency != Currency)
        {
            return Result.Failure(OrderErrors.CurrencyMismatch(Currency, unitPrice.Currency));
        }

        var existing = _lines.SingleOrDefault(line => line.Product == product);
        if (existing is null)
        {
            if (_lines.Count == MaxLines)
            {
                return Result.Failure(OrderErrors.TooManyLines);
            }

            _lines.Add(OrderLine.Create(product, quantity, unitPrice));
            return Result.Success();
        }

        if (existing.UnitPrice != unitPrice)
        {
            return Result.Failure(OrderErrors.PriceDiffers(product));
        }

        if (!Quantity.CanAdd(existing.Quantity, quantity))
        {
            return Result.Failure(OrderErrors.QuantityTooLarge(product));
        }

        existing.IncreaseQuantity(quantity);
        return Result.Success();
    }

    public Result RemoveLine(ProductCode product)
    {
        if (Status != OrderStatus.Draft)
        {
            return Result.Failure(OrderErrors.NotDraft(Status));
        }

        var line = _lines.SingleOrDefault(l => l.Product == product);
        if (line is null)
        {
            return Result.Failure(OrderErrors.LineNotFound(product));
        }

        _lines.Remove(line);
        return Result.Success();
    }

    public Result Place(ShippingAddress shippingAddress, DateTimeOffset now)
    {
        if (Status != OrderStatus.Draft)
        {
            return Result.Failure(OrderErrors.NotDraft(Status));
        }

        if (_lines.Count == 0)
        {
            return Result.Failure(OrderErrors.Empty);
        }

        Status = OrderStatus.Placed;
        ShippingAddress = shippingAddress;
        PlacedAt = now;

        var lines = _lines.Select(l => new OrderPlaced.Line(l.Product, l.Quantity, l.UnitPrice)).ToList();
        Raise(new OrderPlaced(Id, CustomerId, Total, lines, shippingAddress, now));
        return Result.Success();
    }

    public Result Cancel(string reason, DateTimeOffset now)
    {
        Invariant.Require(!string.IsNullOrWhiteSpace(reason), "Cancelling an order requires a reason.");

        switch (Status)
        {
            case OrderStatus.Cancelled:
                return Result.Failure(OrderErrors.AlreadyCancelled);
            case OrderStatus.Shipped:
                return Result.Failure(OrderErrors.AlreadyShipped);
        }

        var wasPlaced = Status == OrderStatus.Placed;
        Status = OrderStatus.Cancelled;
        CancellationReason = reason.Trim();
        Raise(new OrderCancelled(Id, CancellationReason, wasPlaced, now));
        return Result.Success();
    }

    public Result MarkShipped(string trackingCode, DateTimeOffset now)
    {
        Invariant.Require(!string.IsNullOrWhiteSpace(trackingCode), "A shipped order has a tracking code.");

        if (Status != OrderStatus.Placed)
        {
            return Result.Failure(OrderErrors.NotPlaced(Status));
        }

        Status = OrderStatus.Shipped;
        TrackingCode = trackingCode;
        Raise(new OrderShipped(Id, trackingCode, now));
        return Result.Success();
    }
}
