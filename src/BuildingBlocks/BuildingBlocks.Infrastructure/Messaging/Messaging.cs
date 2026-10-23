using System.Reflection;
using System.Text.Json;
using BuildingBlocks.Contracts;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Messaging;

/// <summary>A published integration event as it travels: the serialized contract plus what consumers need to route and deduplicate it.</summary>
public sealed record MessageEnvelope(Guid MessageId, string Type, string Payload, string? MessageGroup, DateTimeOffset OccurredAt);

/// <summary>
/// Transports envelopes to subscribers. The sample uses <see cref="InMemoryMessageBus"/>; replace it
/// with an implementation over a broker (Azure Service Bus, RabbitMQ, ...) without touching anything
/// else. Map <see cref="MessageEnvelope.MessageGroup"/> to the broker's session or partition key to
/// keep per-group ordering.
/// </summary>
public interface IMessageBus
{
    Task PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken);
}

public sealed class IntegrationEventOptions
{
    public HashSet<Assembly> ContractAssemblies { get; } = [];
}

/// <summary>Maps stable integration event names to contract types and back.</summary>
public sealed class IntegrationEventTypes
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _byType = [];

    public IntegrationEventTypes(IOptions<IntegrationEventOptions> options)
    {
        var types = options.Value.ContractAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(t));

        foreach (var type in types)
        {
            var name = NameOf(type);
            if (_byName.TryGetValue(name, out var existing))
            {
                throw new InvalidOperationException($"Integration event name '{name}' is used by both {existing.FullName} and {type.FullName}.");
            }

            _byName[name] = type;
            _byType[type] = name;
        }
    }

    public static string NameOf(Type type) => type.GetCustomAttribute<IntegrationEventNameAttribute>()?.Name ?? type.FullName!;

    public string GetName(Type type) => _byType.TryGetValue(type, out var name)
        ? name
        : throw new InvalidOperationException($"{type.FullName} is not a registered integration event. Register its assembly with AddIntegrationEvents().");

    public Type GetType(string name) => _byName.TryGetValue(name, out var type)
        ? type
        : throw new InvalidOperationException($"No integration event is registered under the name '{name}'.");

    public object Deserialize(MessageEnvelope envelope) =>
        JsonSerializer.Deserialize(envelope.Payload, GetType(envelope.Type), SerializerOptions)
        ?? throw new InvalidOperationException($"Message {envelope.MessageId} has an empty payload.");
}
