using BuildingBlocks.Infrastructure.DomainEvents;
using BuildingBlocks.Infrastructure.Outbox;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering.Domain.Orders;

namespace Ordering.Infrastructure;

public sealed class OrderingDbContext(
    DbContextOptions<OrderingDbContext> options,
    IDomainEventDispatcher dispatcher,
    OutboxCollector outbox,
    OutboxSignal signal) : DbContextBase(options, dispatcher, outbox, signal)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override string TablePrefix => "Ordering";

    protected override void ConfigureModel(ModelBuilder modelBuilder) => modelBuilder.ApplyConfiguration(new OrderConfiguration());
}

/// <summary>
/// Maps the order aggregate. Everything inside it (lines, prices, the address) is an owned type:
/// it is loaded and saved with the order, and a change inside it counts as a change to the order.
/// </summary>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> order)
    {
        order.ToTable("Orders");
        order.HasKey(o => o.Id);
        order.Property(o => o.Id).HasConversion(id => id.Value, value => new OrderId(value)).ValueGeneratedNever();
        order.Property(o => o.CustomerId).HasConversion(id => id.Value, value => new CustomerId(value));
        order.Property(o => o.Currency).HasMaxLength(3);
        order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        order.Property(o => o.CancellationReason).HasMaxLength(500);
        order.Property(o => o.TrackingCode).HasMaxLength(50);
        order.Ignore(o => o.Total);

        order.OwnsOne(o => o.ShippingAddress, address =>
        {
            address.Property(a => a.Street).HasColumnName("ShippingStreet").HasMaxLength(200);
            address.Property(a => a.PostalCode).HasColumnName("ShippingPostalCode").HasMaxLength(20);
            address.Property(a => a.City).HasColumnName("ShippingCity").HasMaxLength(100);
            address.Property(a => a.CountryCode).HasColumnName("ShippingCountryCode").HasMaxLength(2);
        });

        order.OwnsMany(o => o.Lines, line =>
        {
            line.ToTable("OrderLines");
            line.WithOwner().HasForeignKey("OrderId");
            line.HasKey(l => l.Id);
            line.Property(l => l.Id).HasConversion(id => id.Value, value => new OrderLineId(value)).ValueGeneratedNever();
            line.Property(l => l.Product).HasConversion(p => p.Value, value => new ProductCode(value)).HasMaxLength(32);
            line.Property(l => l.Quantity).HasConversion(q => q.Value, value => new Quantity(value));
            line.OwnsOne(l => l.UnitPrice, price =>
            {
                price.Property(p => p.Amount).HasColumnName("UnitPrice").HasPrecision(18, 2);
                price.Property(p => p.Currency).HasColumnName("Currency").HasMaxLength(3);
            });
            line.Ignore(l => l.LineTotal);
        });

        order.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
