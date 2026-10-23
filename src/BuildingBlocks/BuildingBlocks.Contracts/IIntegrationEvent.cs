namespace BuildingBlocks.Contracts;

/// <summary>
/// A fact one bounded context publishes for others. Unlike a domain event it is a public contract:
/// once published, its shape can only grow, and its name never changes.
/// </summary>
public interface IIntegrationEvent
{
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// The stable name under which an integration event is stored and transported, such as
/// <c>ordering.order-placed.v1</c>. Without it the type's full name is used, which breaks stored
/// messages the day someone renames or moves the type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
