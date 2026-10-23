namespace BuildingBlocks.Domain;

/// <summary>
/// Something meaningful that happened in the domain, named in the past tense: <c>OrderPlaced</c>,
/// not <c>PlaceOrder</c>. Immutable, and complete enough that a handler never has to look anything up
/// to know what happened.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
