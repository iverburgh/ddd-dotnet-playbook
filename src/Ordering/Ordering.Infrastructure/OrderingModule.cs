using BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application;
using Ordering.Contracts;
using Ordering.Domain.Orders;
using Shipping.Contracts;

namespace Ordering.Infrastructure;

public static class OrderingModule
{
    /// <summary>Everything the Ordering context needs, behind one call. The host chooses the database.</summary>
    public static IServiceCollection AddOrderingModule(this IServiceCollection services, Action<DbContextOptionsBuilder> configureDatabase)
    {
        services.AddBuildingBlocks();
        services.AddDbContext<OrderingDbContext>(configureDatabase);

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderQueries, OrderQueries>();
        services.AddScoped<StartOrderHandler>();
        services.AddScoped<AddOrderLineHandler>();
        services.AddScoped<RemoveOrderLineHandler>();
        services.AddScoped<PlaceOrderHandler>();
        services.AddScoped<CancelOrderHandler>();

        services.AddDomainEventHandlers(typeof(PublishOrderPlaced).Assembly);
        services.AddIntegrationEvents(typeof(OrderPlacedIntegrationEvent).Assembly);
        services.AddIntegrationEvents(typeof(ShipmentDispatchedIntegrationEvent).Assembly);
        services.AddIntegrationEventHandler<ShipmentDispatchedIntegrationEvent, MarkOrderShippedWhenShipmentDispatched, OrderingDbContext>();
        services.AddOutboxPublisher<OrderingDbContext>();
        return services;
    }
}
