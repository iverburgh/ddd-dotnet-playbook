using BuildingBlocks.Application;
using BuildingBlocks.Domain;
using Microsoft.Extensions.Logging;
using Ordering.Contracts;
using Shipping.Contracts;
using Shipping.Domain.Shipments;

namespace Shipping.Application;

public static class ShippingErrors
{
    public static Error ShipmentNotFound(Guid shipmentId) => new("shipment.not-found", $"Shipment {shipmentId} does not exist.");
}

public sealed record DispatchShipment(Guid ShipmentId, string TrackingCode);

public sealed class DispatchShipmentHandler(IShipmentRepository shipments, TimeProvider timeProvider)
{
    public async Task<Result> Handle(DispatchShipment command, CancellationToken cancellationToken)
    {
        var shipment = await shipments.GetAsync(new ShipmentId(command.ShipmentId), cancellationToken);
        if (shipment is null)
        {
            return Result.Failure(ShippingErrors.ShipmentNotFound(command.ShipmentId));
        }

        var result = shipment.Dispatch(new TrackingCode(command.TrackingCode), timeProvider.GetUtcNow());
        if (result.IsSuccess)
        {
            await shipments.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

/// <summary>An order was placed in Ordering; Shipping starts preparing the parcel.</summary>
public sealed class PrepareShipmentWhenOrderPlaced(IShipmentRepository shipments, TimeProvider timeProvider)
    : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public async Task Handle(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var order = new OrderReference(integrationEvent.OrderId);

        // The inbox already filters redelivered messages; this also makes the handler naturally idempotent.
        if (await shipments.FindByOrderAsync(order, cancellationToken) is not null)
        {
            return;
        }

        var address = integrationEvent.ShippingAddress;
        var shipment = Shipment.Prepare(
            ShipmentId.New(),
            order,
            new DeliveryAddress(address.Street, address.PostalCode, address.City, address.CountryCode),
            integrationEvent.Lines.Select(l => new ShipmentItem(l.ProductCode, l.Quantity)),
            timeProvider.GetUtcNow());

        shipments.Add(shipment);
        await shipments.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>An order was cancelled; stop the parcel if it has not left yet.</summary>
public sealed partial class CancelShipmentWhenOrderCancelled(
    IShipmentRepository shipments,
    TimeProvider timeProvider,
    ILogger<CancelShipmentWhenOrderCancelled> logger) : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    public async Task Handle(OrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var shipment = await shipments.FindByOrderAsync(new OrderReference(integrationEvent.OrderId), cancellationToken);
        if (shipment is null)
        {
            return;
        }

        var result = shipment.Cancel(timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            // The parcel left before the cancellation arrived. That is a business situation, not a
            // technical error: report it, and let a person or a follow-up process (a return) handle it.
            LogTooLateToCancel(integrationEvent.OrderId, result.Error!.Code);
            return;
        }

        await shipments.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} was cancelled but its shipment could not be stopped: {ErrorCode}.")]
    private partial void LogTooLateToCancel(Guid orderId, string errorCode);
}

public sealed class PublishShipmentDispatched(IOutbox outbox) : IDomainEventHandler<ShipmentDispatched>
{
    public Task Handle(ShipmentDispatched domainEvent, CancellationToken cancellationToken)
    {
        outbox.Enqueue(
            new ShipmentDispatchedIntegrationEvent(domainEvent.ShipmentId.Value, domainEvent.Order.Value, domainEvent.TrackingCode.Value, domainEvent.OccurredAt),
            messageGroup: $"shipment-{domainEvent.ShipmentId}");
        return Task.CompletedTask;
    }
}

public sealed record ShipmentDetails(Guid Id, Guid OrderId, string Status, string? TrackingCode, IReadOnlyList<ShipmentItemDetails> Items);

public sealed record ShipmentItemDetails(string ProductCode, int Quantity);

public interface IShipmentQueries
{
    Task<IReadOnlyList<ShipmentDetails>> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken);
}
