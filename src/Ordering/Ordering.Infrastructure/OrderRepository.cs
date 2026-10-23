using Microsoft.EntityFrameworkCore;
using Ordering.Application;
using Ordering.Domain.Orders;

namespace Ordering.Infrastructure;

/// <summary>Loads and saves whole orders: always the root with everything inside it, never a loose line.</summary>
internal sealed class OrderRepository(OrderingDbContext db) : IOrderRepository
{
    public Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken) =>
        db.Orders.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public void Add(Order order) => db.Orders.Add(order);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

internal sealed class OrderQueries(OrderingDbContext db) : IOrderQueries
{
    public async Task<OrderDetails?> GetAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var id = new OrderId(orderId);
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return null;
        }

        return new OrderDetails(
            order.Id.Value,
            order.CustomerId.Value,
            order.Status.ToString(),
            order.Currency,
            order.Total.Amount,
            [.. order.Lines.Select(l => new OrderLineDetails(l.Product.Value, l.Quantity.Value, l.UnitPrice.Amount, l.LineTotal.Amount))],
            order.TrackingCode,
            order.CancellationReason,
            order.Version);
    }
}
