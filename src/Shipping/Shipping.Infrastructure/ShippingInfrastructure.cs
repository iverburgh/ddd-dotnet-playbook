using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.DomainEvents;
using BuildingBlocks.Infrastructure.Outbox;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Contracts;
using Shipping.Application;
using Shipping.Contracts;
using Shipping.Domain.Shipments;

namespace Shipping.Infrastructure;

public sealed class ShippingDbContext(
    DbContextOptions<ShippingDbContext> options,
    IDomainEventDispatcher dispatcher,
    OutboxCollector outbox,
    OutboxSignal signal) : DbContextBase(options, dispatcher, outbox, signal)
{
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override string TablePrefix => "Shipping";

    protected override void ConfigureModel(ModelBuilder modelBuilder) => modelBuilder.ApplyConfiguration(new ShipmentConfiguration());
}

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> shipment)
    {
        shipment.ToTable("Shipments");
        shipment.HasKey(s => s.Id);
        shipment.Property(s => s.Id).HasConversion(id => id.Value, value => new ShipmentId(value)).ValueGeneratedNever();
        shipment.Property(s => s.Order).HasColumnName("OrderId").HasConversion(o => o.Value, value => new OrderReference(value));
        shipment.HasIndex(s => s.Order).IsUnique();
        shipment.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        shipment.Property(s => s.TrackingCode).HasConversion(t => t!.Value, value => new TrackingCode(value)).HasMaxLength(30);

        shipment.OwnsOne(s => s.DeliveryAddress, address =>
        {
            address.Property(a => a.Street).HasColumnName("DeliveryStreet").HasMaxLength(200);
            address.Property(a => a.PostalCode).HasColumnName("DeliveryPostalCode").HasMaxLength(20);
            address.Property(a => a.City).HasColumnName("DeliveryCity").HasMaxLength(100);
            address.Property(a => a.CountryCode).HasColumnName("DeliveryCountryCode").HasMaxLength(2);
        });

        // A collection of value objects: stored in its own table with a key the domain never sees.
        shipment.OwnsMany(s => s.Items, item =>
        {
            item.ToTable("ShipmentItems");
            item.WithOwner().HasForeignKey("ShipmentId");
            item.Property<int>("Id");
            item.HasKey("Id");
            // Get-only properties are not mapped by convention: list them explicitly.
            item.Property(i => i.ProductCode).HasMaxLength(32);
            item.Property(i => i.Quantity);
        });

        shipment.Navigation(s => s.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ShipmentRepository(ShippingDbContext db) : IShipmentRepository
{
    public Task<Shipment?> GetAsync(ShipmentId id, CancellationToken cancellationToken) =>
        db.Shipments.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Shipment?> FindByOrderAsync(OrderReference order, CancellationToken cancellationToken) =>
        db.Shipments.SingleOrDefaultAsync(s => s.Order == order, cancellationToken);

    public void Add(Shipment shipment) => db.Shipments.Add(shipment);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

internal sealed class ShipmentQueries(ShippingDbContext db) : IShipmentQueries
{
    public async Task<IReadOnlyList<ShipmentDetails>> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = new OrderReference(orderId);
        var shipments = await db.Shipments.AsNoTracking().Where(s => s.Order == order).ToListAsync(cancellationToken);
        return
        [
            .. shipments.Select(s => new ShipmentDetails(
                s.Id.Value,
                s.Order.Value,
                s.Status.ToString(),
                s.TrackingCode?.Value,
                [.. s.Items.Select(i => new ShipmentItemDetails(i.ProductCode, i.Quantity))])),
        ];
    }
}

public static class ShippingModule
{
    public static IServiceCollection AddShippingModule(this IServiceCollection services, Action<DbContextOptionsBuilder> configureDatabase)
    {
        services.AddBuildingBlocks();
        services.AddDbContext<ShippingDbContext>(configureDatabase);

        services.AddScoped<IShipmentRepository, ShipmentRepository>();
        services.AddScoped<IShipmentQueries, ShipmentQueries>();
        services.AddScoped<DispatchShipmentHandler>();

        services.AddDomainEventHandlers(typeof(PublishShipmentDispatched).Assembly);
        services.AddIntegrationEvents(typeof(ShipmentDispatchedIntegrationEvent).Assembly);
        services.AddIntegrationEvents(typeof(OrderPlacedIntegrationEvent).Assembly);
        services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, PrepareShipmentWhenOrderPlaced, ShippingDbContext>();
        services.AddIntegrationEventHandler<OrderCancelledIntegrationEvent, CancelShipmentWhenOrderCancelled, ShippingDbContext>();
        services.AddOutboxPublisher<ShippingDbContext>();
        return services;
    }
}
