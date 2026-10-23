using BuildingBlocks.Application;
using Microsoft.Extensions.Logging;
using Ordering.Contracts;
using Ordering.Domain.Orders;
using Shipping.Contracts;

namespace Ordering.Application;

/// <summary>
/// Translates the internal <see cref="OrderPlaced"/> into the public contract and puts it in the
/// outbox. It is stored in the same transaction as the order, and published after the commit.
/// The order id is the message group, so consumers see the events of one order in order.
/// </summary>
public sealed class PublishOrderPlaced(IOutbox outbox) : IDomainEventHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced domainEvent, CancellationToken cancellationToken)
    {
        var address = domainEvent.ShippingAddress;
        outbox.Enqueue(
            new OrderPlacedIntegrationEvent(
                domainEvent.OrderId.Value,
                domainEvent.CustomerId.Value,
                [.. domainEvent.Lines.Select(l => new OrderPlacedIntegrationEvent.Line(l.Product.Value, l.Quantity.Value))],
                new OrderPlacedIntegrationEvent.Address(address.Street, address.PostalCode, address.City, address.CountryCode),
                domainEvent.OccurredAt),
            messageGroup: $"order-{domainEvent.OrderId}");
        return Task.CompletedTask;
    }
}

public sealed class PublishOrderCancelled(IOutbox outbox) : IDomainEventHandler<OrderCancelled>
{
    public Task Handle(OrderCancelled domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.WasPlaced)
        {
            outbox.Enqueue(
                new OrderCancelledIntegrationEvent(domainEvent.OrderId.Value, domainEvent.Reason, domainEvent.OccurredAt),
                messageGroup: $"order-{domainEvent.OrderId}");
        }

        return Task.CompletedTask;
    }
}

/// <summary>Shipping tells us the parcel left the warehouse; the order follows.</summary>
public sealed partial class MarkOrderShippedWhenShipmentDispatched(
    IOrderRepository orders,
    ILogger<MarkOrderShippedWhenShipmentDispatched> logger) : IIntegrationEventHandler<ShipmentDispatchedIntegrationEvent>
{
    public async Task Handle(ShipmentDispatchedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(new OrderId(integrationEvent.OrderId), cancellationToken)
            ?? throw new InvalidOperationException($"Shipment {integrationEvent.ShipmentId} refers to unknown order {integrationEvent.OrderId}.");

        var result = order.MarkShipped(integrationEvent.TrackingCode, integrationEvent.OccurredAt);
        if (result.IsFailure)
        {
            // A fact from another context cannot be refused. Throwing would only retry forever, so
            // record the conflict for a person to resolve (refund, recall the parcel, ...).
            LogShipmentConflict(integrationEvent.OrderId, result.Error!.Code);
            return;
        }

        await orders.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} was dispatched but could not be marked shipped: {ErrorCode}.")]
    private partial void LogShipmentConflict(Guid orderId, string errorCode);
}
