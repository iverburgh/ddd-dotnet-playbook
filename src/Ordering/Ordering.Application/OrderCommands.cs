using BuildingBlocks.Domain;
using Ordering.Domain.Orders;

namespace Ordering.Application;

/// <summary>
/// The use cases of Ordering. Each handler loads one aggregate, asks it to do one thing, and saves
/// it. Business decisions stay in the aggregate; the handler only orchestrates.
/// </summary>
public static class OrderingErrors
{
    public static Error OrderNotFound(Guid orderId) => new("order.not-found", $"Order {orderId} does not exist.");
}

public sealed record StartOrder(Guid CustomerId, string Currency);

public sealed class StartOrderHandler(IOrderRepository orders, TimeProvider timeProvider)
{
    public async Task<Guid> Handle(StartOrder command, CancellationToken cancellationToken)
    {
        var order = Order.Start(OrderId.New(), new CustomerId(command.CustomerId), command.Currency, timeProvider.GetUtcNow());
        orders.Add(order);
        await orders.SaveChangesAsync(cancellationToken);
        return order.Id.Value;
    }
}

public sealed record AddOrderLine(Guid OrderId, string ProductCode, int Quantity, decimal UnitPrice, string Currency);

public sealed class AddOrderLineHandler(IOrderRepository orders)
{
    public async Task<Result> Handle(AddOrderLine command, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(OrderingErrors.OrderNotFound(command.OrderId));
        }

        var result = order.AddLine(
            new ProductCode(command.ProductCode),
            new Quantity(command.Quantity),
            new Money(command.UnitPrice, command.Currency));

        if (result.IsSuccess)
        {
            await orders.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public sealed record RemoveOrderLine(Guid OrderId, string ProductCode);

public sealed class RemoveOrderLineHandler(IOrderRepository orders)
{
    public async Task<Result> Handle(RemoveOrderLine command, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(OrderingErrors.OrderNotFound(command.OrderId));
        }

        var result = order.RemoveLine(new ProductCode(command.ProductCode));
        if (result.IsSuccess)
        {
            await orders.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public sealed record PlaceOrder(Guid OrderId, string Street, string PostalCode, string City, string CountryCode);

public sealed class PlaceOrderHandler(IOrderRepository orders, TimeProvider timeProvider)
{
    public async Task<Result> Handle(PlaceOrder command, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(OrderingErrors.OrderNotFound(command.OrderId));
        }

        var address = new ShippingAddress(command.Street, command.PostalCode, command.City, command.CountryCode);
        var result = order.Place(address, timeProvider.GetUtcNow());
        if (result.IsSuccess)
        {
            await orders.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public sealed record CancelOrder(Guid OrderId, string Reason);

public sealed class CancelOrderHandler(IOrderRepository orders, TimeProvider timeProvider)
{
    public async Task<Result> Handle(CancelOrder command, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(OrderingErrors.OrderNotFound(command.OrderId));
        }

        var result = order.Cancel(command.Reason, timeProvider.GetUtcNow());
        if (result.IsSuccess)
        {
            await orders.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
