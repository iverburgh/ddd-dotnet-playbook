using BuildingBlocks.Contracts;

namespace Shipping.Contracts;

/// <summary>Published when the carrier has picked up the shipment for an order.</summary>
[IntegrationEventName("shipping.shipment-dispatched.v1")]
public sealed record ShipmentDispatchedIntegrationEvent(Guid ShipmentId, Guid OrderId, string TrackingCode, DateTimeOffset OccurredAt) : IIntegrationEvent;
