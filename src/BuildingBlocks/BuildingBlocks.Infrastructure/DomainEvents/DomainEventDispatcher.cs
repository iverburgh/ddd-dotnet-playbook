using System.Collections.Concurrent;
using System.Reflection;
using BuildingBlocks.Application;
using BuildingBlocks.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.DomainEvents;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>Calls every registered <see cref="IDomainEventHandler{TEvent}"/> for the exact type of an event, in the current scope.</summary>
internal sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Func<object, IDomainEvent, CancellationToken, Task>> Invokers = new();

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var eventType = domainEvent.GetType();
        var invoke = Invokers.GetOrAdd(eventType, CreateInvoker);
        foreach (var handler in services.GetServices(typeof(IDomainEventHandler<>).MakeGenericType(eventType)))
        {
            await invoke(handler!, domainEvent, cancellationToken);
        }
    }

    private static Func<object, IDomainEvent, CancellationToken, Task> CreateInvoker(Type eventType) =>
        typeof(DomainEventDispatcher)
            .GetMethod(nameof(Invoke), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(eventType)
            .CreateDelegate<Func<object, IDomainEvent, CancellationToken, Task>>();

    private static Task Invoke<TEvent>(object handler, IDomainEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent =>
        ((IDomainEventHandler<TEvent>)handler).Handle((TEvent)domainEvent, cancellationToken);
}
