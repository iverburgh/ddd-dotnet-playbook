using BuildingBlocks.Domain;

namespace Shipping.Domain.Shipments;

public enum ShipmentStatus
{
    Pending,
    Dispatched,
    Cancelled,
}

public sealed record ShipmentDispatched(ShipmentId ShipmentId, OrderReference Order, TrackingCode TrackingCode, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ShipmentCancelled(ShipmentId ShipmentId, OrderReference Order, DateTimeOffset OccurredAt) : IDomainEvent;

public static class ShipmentErrors
{
    public static readonly Error AlreadyDispatched = new("shipment.already-dispatched", "The shipment has already left the warehouse.");

    public static readonly Error AlreadyCancelled = new("shipment.already-cancelled", "The shipment is cancelled.");
}

/// <summary>
/// The parcel for one order, from packing to hand-over to the carrier.
/// <list type="bullet">
/// <item>A shipment always contains at least one item.</item>
/// <item>It is dispatched once, and once dispatched it cannot be cancelled.</item>
/// </list>
/// </summary>
public sealed class Shipment : AggregateRoot<ShipmentId>
{
    private readonly List<ShipmentItem> _items = [];

    private Shipment(ShipmentId id, OrderReference order, DeliveryAddress address, List<ShipmentItem> items, DateTimeOffset createdAt)
        : base(id)
    {
        Order = order;
        DeliveryAddress = address;
        _items = items;
        Status = ShipmentStatus.Pending;
        CreatedAt = createdAt;
    }

    // For the ORM only.
    private Shipment()
    {
        Order = null!;
        DeliveryAddress = null!;
    }

    public OrderReference Order { get; private set; }

    public DeliveryAddress DeliveryAddress { get; private set; }

    public IReadOnlyCollection<ShipmentItem> Items => _items.AsReadOnly();

    public ShipmentStatus Status { get; private set; }

    public TrackingCode? TrackingCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DispatchedAt { get; private set; }

    public static Shipment Prepare(ShipmentId id, OrderReference order, DeliveryAddress address, IEnumerable<ShipmentItem> items, DateTimeOffset now)
    {
        var list = items.ToList();
        Invariant.Require(list.Count > 0, "A shipment contains at least one item.");
        return new Shipment(id, order, address, list, now);
    }

    public Result Dispatch(TrackingCode trackingCode, DateTimeOffset now)
    {
        switch (Status)
        {
            case ShipmentStatus.Dispatched:
                return Result.Failure(ShipmentErrors.AlreadyDispatched);
            case ShipmentStatus.Cancelled:
                return Result.Failure(ShipmentErrors.AlreadyCancelled);
        }

        Status = ShipmentStatus.Dispatched;
        TrackingCode = trackingCode;
        DispatchedAt = now;
        Raise(new ShipmentDispatched(Id, Order, trackingCode, now));
        return Result.Success();
    }

    public Result Cancel(DateTimeOffset now)
    {
        switch (Status)
        {
            case ShipmentStatus.Dispatched:
                return Result.Failure(ShipmentErrors.AlreadyDispatched);
            case ShipmentStatus.Cancelled:
                return Result.Success(); // Cancelling twice changes nothing; idempotent on purpose.
        }

        Status = ShipmentStatus.Cancelled;
        Raise(new ShipmentCancelled(Id, Order, now));
        return Result.Success();
    }
}

public interface IShipmentRepository
{
    Task<Shipment?> GetAsync(ShipmentId id, CancellationToken cancellationToken);

    Task<Shipment?> FindByOrderAsync(OrderReference order, CancellationToken cancellationToken);

    void Add(Shipment shipment);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
