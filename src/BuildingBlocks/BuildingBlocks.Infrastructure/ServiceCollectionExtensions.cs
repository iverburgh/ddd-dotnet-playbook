using System.Reflection;
using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure.DomainEvents;
using BuildingBlocks.Infrastructure.Messaging;
using BuildingBlocks.Infrastructure.Outbox;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Shared plumbing: domain event dispatch, the outbox collector and signal, integration event types and the in-memory bus.</summary>
    public static IServiceCollection AddBuildingBlocks(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OutboxSignal>();
        services.TryAddSingleton<IntegrationEventTypes>();
        services.TryAddSingleton<IMessageBus, InMemoryMessageBus>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.TryAddScoped<OutboxCollector>();
        services.TryAddScoped<IOutbox>(sp => sp.GetRequiredService<OutboxCollector>());
        services.AddOptions<OutboxOptions>();
        return services;
    }

    /// <summary>Registers every integration event contract in an assembly, published or consumed.</summary>
    public static IServiceCollection AddIntegrationEvents(this IServiceCollection services, Assembly contractAssembly) =>
        services.Configure<IntegrationEventOptions>(options => options.ContractAssemblies.Add(contractAssembly));

    /// <summary>Registers every <see cref="IDomainEventHandler{TEvent}"/> implementation in an assembly.</summary>
    public static IServiceCollection AddDomainEventHandlers(this IServiceCollection services, Assembly assembly)
    {
        var registrations = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>))
                .Select(i => (Service: i, Implementation: t)));

        foreach (var (service, implementation) in registrations)
        {
            services.AddScoped(service, implementation);
        }

        return services;
    }

    /// <summary>Subscribes a handler, deduplicated through the inbox of <typeparamref name="TContext"/>.</summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler, TContext>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
        where TContext : DbContextBase
    {
        services.AddScoped<THandler>();
        services.AddSingleton<IIntegrationEventSubscription, IntegrationEventSubscription<TEvent, THandler, TContext>>();
        return services;
    }

    /// <summary>Publishes the outbox of <typeparamref name="TContext"/> in the background.</summary>
    public static IServiceCollection AddOutboxPublisher<TContext>(this IServiceCollection services)
        where TContext : DbContextBase
    {
        services.AddScoped<OutboxProcessor<TContext>>();
        services.AddHostedService<OutboxPublisherService<TContext>>();
        return services;
    }
}
