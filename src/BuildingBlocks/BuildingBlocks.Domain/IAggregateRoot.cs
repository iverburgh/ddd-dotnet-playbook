namespace BuildingBlocks.Domain;

/// <summary>What persistence needs from any aggregate root, regardless of its id type.</summary>
public interface IAggregateRoot
{
    /// <summary>Optimistic concurrency token, incremented by persistence whenever anything in the aggregate changes.</summary>
    int Version { get; }

    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
