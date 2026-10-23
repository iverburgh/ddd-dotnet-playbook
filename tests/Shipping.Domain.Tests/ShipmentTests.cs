using BuildingBlocks.Domain;
using Shipping.Domain.Shipments;

namespace Shipping.Domain.Tests;

public sealed class ShipmentTests
{
    private static readonly DateTimeOffset Now = new(2025, 3, 2, 14, 0, 0, TimeSpan.Zero);
    private static readonly TrackingCode Tracking = new("TRACK12345NL");

    private static Shipment PendingShipment() => Shipment.Prepare(
        ShipmentId.New(),
        new OrderReference(Guid.NewGuid()),
        new DeliveryAddress("Main Street 1", "1234 AB", "Springfield", "NL"),
        [new ShipmentItem("BOOK-1984", 2)],
        Now);

    [Test]
    public void A_prepared_shipment_is_pending()
    {
        var shipment = PendingShipment();

        Assert.That(shipment.Status, Is.EqualTo(ShipmentStatus.Pending));
        Assert.That(shipment.Items.Single(), Is.EqualTo(new ShipmentItem("BOOK-1984", 2)));
    }

    [Test]
    public void A_shipment_without_items_cannot_exist() =>
        Assert.Throws<InvariantViolationException>(() => Shipment.Prepare(
            ShipmentId.New(),
            new OrderReference(Guid.NewGuid()),
            new DeliveryAddress("Main Street 1", "1234 AB", "Springfield", "NL"),
            [],
            Now));

    [Test]
    public void Dispatching_raises_ShipmentDispatched()
    {
        var shipment = PendingShipment();

        var result = shipment.Dispatch(Tracking, Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(shipment.Status, Is.EqualTo(ShipmentStatus.Dispatched));
        Assert.That(shipment.DomainEvents.Single(), Is.EqualTo(new ShipmentDispatched(shipment.Id, shipment.Order, Tracking, Now)));
    }

    [Test]
    public void A_shipment_is_dispatched_only_once()
    {
        var shipment = PendingShipment();
        shipment.Dispatch(Tracking, Now);
        shipment.ClearDomainEvents();

        var result = shipment.Dispatch(new TrackingCode("OTHER1234NL"), Now);

        Assert.That(result.Error, Is.EqualTo(ShipmentErrors.AlreadyDispatched));
        Assert.That(shipment.TrackingCode, Is.EqualTo(Tracking));
        Assert.That(shipment.DomainEvents, Is.Empty);
    }

    [Test]
    public void A_dispatched_shipment_cannot_be_cancelled()
    {
        var shipment = PendingShipment();
        shipment.Dispatch(Tracking, Now);

        Assert.That(shipment.Cancel(Now).Error, Is.EqualTo(ShipmentErrors.AlreadyDispatched));
    }

    [Test]
    public void Cancelling_twice_is_harmless_and_raises_one_event()
    {
        var shipment = PendingShipment();

        shipment.Cancel(Now);
        var second = shipment.Cancel(Now);

        Assert.That(second.IsSuccess, Is.True);
        Assert.That(shipment.DomainEvents.OfType<ShipmentCancelled>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void A_cancelled_shipment_cannot_be_dispatched()
    {
        var shipment = PendingShipment();
        shipment.Cancel(Now);

        Assert.That(shipment.Dispatch(Tracking, Now).Error, Is.EqualTo(ShipmentErrors.AlreadyCancelled));
    }

    [TestCase("SHORT")]
    [TestCase("track12345nl")]
    public void A_tracking_code_has_a_fixed_shape(string value) =>
        Assert.Throws<InvariantViolationException>(() => _ = new TrackingCode(value));
}
