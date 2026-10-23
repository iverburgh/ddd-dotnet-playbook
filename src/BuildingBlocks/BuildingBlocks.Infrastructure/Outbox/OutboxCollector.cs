using System.Text.Json;
using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure.Messaging;

namespace BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Scoped buffer behind <see cref="IOutbox"/>. Domain event handlers fill it while
/// <see cref="Persistence.DbContextBase"/> dispatches events; the context then drains it into its
/// own outbox table in the same transaction.
/// </summary>
public sealed class OutboxCollector(IntegrationEventTypes types, TimeProvider timeProvider) : IOutbox
{
    private readonly List<OutboxMessage> _pending = [];

    public void Enqueue(IIntegrationEvent integrationEvent, string? messageGroup = null)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var type = integrationEvent.GetType();
        _pending.Add(OutboxMessage.Create(
            types.GetName(type),
            JsonSerializer.Serialize(integrationEvent, type, IntegrationEventTypes.SerializerOptions),
            messageGroup,
            integrationEvent.OccurredAt,
            timeProvider.GetUtcNow()));
    }

    internal IReadOnlyList<OutboxMessage> Drain()
    {
        var drained = _pending.ToList();
        _pending.Clear();
        return drained;
    }
}
