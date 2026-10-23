using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure.Inbox;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.Messaging;

/// <summary>One handler in one bounded context listening to one integration event.</summary>
public interface IIntegrationEventSubscription
{
    Type EventType { get; }

    Task DeliverAsync(IServiceProvider services, MessageEnvelope envelope, object integrationEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Delivers a message to its handler exactly once per handler, using the inbox of the handler's own
/// bounded context. The inbox record is added before the handler runs and saved in the same
/// transaction as the handler's changes: a redelivered message finds it and is skipped, a failed
/// handler leaves no record and is retried.
/// </summary>
internal sealed class IntegrationEventSubscription<TEvent, THandler, TContext> : IIntegrationEventSubscription
    where TEvent : IIntegrationEvent
    where THandler : IIntegrationEventHandler<TEvent>
    where TContext : DbContextBase
{
    private static readonly string HandlerName = typeof(THandler).FullName!;

    public Type EventType => typeof(TEvent);

    public async Task DeliverAsync(IServiceProvider services, MessageEnvelope envelope, object integrationEvent, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var alreadyHandled = await db.Set<InboxMessage>()
            .AnyAsync(m => m.MessageId == envelope.MessageId && m.Handler == HandlerName, cancellationToken);
        if (alreadyHandled)
        {
            return;
        }

        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        db.Set<InboxMessage>().Add(new InboxMessage(envelope.MessageId, HandlerName, now));

        await scope.ServiceProvider.GetRequiredService<THandler>().Handle((TEvent)integrationEvent, cancellationToken);

        // The handler normally saves through its repository; this saves the inbox record when it had nothing to change.
        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// Delivers each message to every subscription in this process. Stands in for a real broker; note
/// that one failing subscriber makes the whole publish fail and the outbox retry it, which is
/// exactly when the inbox earns its keep for the subscribers that had already succeeded.
/// </summary>
internal sealed class InMemoryMessageBus(
    IServiceProvider services,
    IntegrationEventTypes types,
    IEnumerable<IIntegrationEventSubscription> subscriptions) : IMessageBus
{
    public async Task PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        var integrationEvent = types.Deserialize(envelope);
        foreach (var subscription in subscriptions.Where(s => s.EventType == integrationEvent.GetType()))
        {
            await subscription.DeliverAsync(services, envelope, integrationEvent, cancellationToken);
        }
    }
}
