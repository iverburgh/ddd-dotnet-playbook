using BuildingBlocks.Contracts;
using BuildingBlocks.Domain;

namespace BuildingBlocks.Application;

/// <summary>
/// Reacts to a domain event inside the same unit of work that raised it, before anything is
/// committed. If a handler throws, nothing is saved.
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Reacts to an integration event from another bounded context. Delivery is at least once, so the
/// infrastructure records every handled message (the inbox) and skips duplicates.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task Handle(TEvent integrationEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Records an integration event to be published once the current unit of work commits. Call it from
/// a domain event handler: the message is stored in the same transaction as the change that caused
/// it, so either both happen or neither does.
/// </summary>
public interface IOutbox
{
    /// <param name="messageGroup">
    /// Messages with the same group are delivered in the order they were enqueued, and a failing
    /// message holds back the rest of its group. Use the aggregate id, so that events about one
    /// aggregate never overtake each other.
    /// </param>
    void Enqueue(IIntegrationEvent integrationEvent, string? messageGroup = null);
}
