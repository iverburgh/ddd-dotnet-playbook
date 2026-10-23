using BuildingBlocks.Contracts;

namespace Ordering.Contracts;

/// <summary>
/// Published when a customer places an order. Carries what other contexts need to act on it and
/// nothing more: Shipping needs the products and the address, not the prices.
/// </summary>
[IntegrationEventName("ordering.order-placed.v1")]
public sealed record OrderPlacedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderPlacedIntegrationEvent.Line> Lines,
    OrderPlacedIntegrationEvent.Address ShippingAddress,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public sealed record Line(string ProductCode, int Quantity);

    public sealed record Address(string Street, string PostalCode, string City, string CountryCode);
}

/// <summary>Published when a placed order is cancelled. A draft that is cancelled was never announced, so it is not published.</summary>
[IntegrationEventName("ordering.order-cancelled.v1")]
public sealed record OrderCancelledIntegrationEvent(Guid OrderId, string Reason, DateTimeOffset OccurredAt) : IIntegrationEvent;
