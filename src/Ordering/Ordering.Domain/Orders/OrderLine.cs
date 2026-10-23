using BuildingBlocks.Domain;

namespace Ordering.Domain.Orders;

/// <summary>One product on an order. Lives only inside its <see cref="Order"/>: code outside the aggregate never changes it directly.</summary>
public sealed class OrderLine : Entity<OrderLineId>
{
    private OrderLine(OrderLineId id, ProductCode product, Quantity quantity, Money unitPrice)
        : base(id)
    {
        Product = product;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    // For the ORM only.
    private OrderLine()
    {
        Product = null!;
        Quantity = null!;
        UnitPrice = null!;
    }

    public ProductCode Product { get; private set; }

    public Quantity Quantity { get; private set; }

    public Money UnitPrice { get; private set; }

    public Money LineTotal => UnitPrice.Multiply(Quantity.Value);

    internal static OrderLine Create(ProductCode product, Quantity quantity, Money unitPrice) =>
        new(OrderLineId.New(), product, quantity, unitPrice);

    internal void IncreaseQuantity(Quantity extra) => Quantity = Quantity.Add(extra);
}
