using System.Reflection;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using Ordering.Contracts;
using Shipping.Contracts;

namespace Architecture.Tests;

/// <summary>
/// Integration events are stored in the outbox and read back later, possibly by a newer version of
/// the code. These tests prove every contract survives that trip through the real serializer, and
/// that a new contract cannot be added without a sample that proves it too.
/// </summary>
public sealed class ContractTests
{
    private const string VersionedName = @"^[a-z]+(-[a-z]+)*\.[a-z]+(-[a-z]+)*\.v[0-9]+$";

    private static readonly Assembly[] ContractAssemblies =
    [
        typeof(OrderPlacedIntegrationEvent).Assembly,
        typeof(ShipmentDispatchedIntegrationEvent).Assembly,
    ];

    private static readonly DateTimeOffset OccurredAt = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrderId = Guid.NewGuid();

    // Fill every property with a non-default value: a property that is lost on the way back then shows up in the comparison.
    private static readonly IIntegrationEvent[] Samples =
    [
        new OrderPlacedIntegrationEvent(
            OrderId,
            Guid.NewGuid(),
            [new OrderPlacedIntegrationEvent.Line("BOOK-001", 2), new OrderPlacedIntegrationEvent.Line("PEN-042", 1)],
            new OrderPlacedIntegrationEvent.Address("1 Main Street", "1234 AB", "Springfield", "NL"),
            OccurredAt),
        new OrderCancelledIntegrationEvent(OrderId, "Customer changed their mind", OccurredAt),
        new ShipmentDispatchedIntegrationEvent(
            Guid.NewGuid(),
            OrderId,
            "TRACK-123",
            OccurredAt),
    ];

    private static readonly IntegrationEventTypes Types = CreateTypes();

    private static IEnumerable<Type> Contracts() => ContractAssemblies
        .SelectMany(a => a.GetTypes())
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(t));

    private static IEnumerable<TestCaseData> SampleCases() =>
        Samples.Select(s => new TestCaseData(s).SetArgDisplayNames(s.GetType().Name));

    private static IEnumerable<TestCaseData> ContractCases() =>
        Contracts().Select(t => new TestCaseData(t).SetArgDisplayNames(t.Name));

    [Test]
    public void Every_contract_has_a_sample()
    {
        var missing = Contracts().Except(Samples.Select(s => s.GetType())).Select(t => t.FullName);

        Assert.That(missing, Is.Empty, "Add a sample for each new integration event, so its round trip is tested.");
    }

    [TestCaseSource(nameof(SampleCases))]
    public void A_contract_reads_back_exactly_as_it_was_stored(IIntegrationEvent sample)
    {
        var name = Types.GetName(sample.GetType());
        var stored = Types.Serialize(sample);

        var readBack = Types.Deserialize(new MessageEnvelope(Guid.NewGuid(), name, stored, null, OccurredAt));

        Assert.That(readBack, Is.TypeOf(sample.GetType()));
        Assert.That(Types.Serialize((IIntegrationEvent)readBack), Is.EqualTo(stored),
            "Compare the JSON, not the records: record equality compares lists by reference.");
    }

    [TestCaseSource(nameof(ContractCases))]
    public void A_contract_has_a_stable_versioned_name(Type contract)
    {
        var name = contract.GetCustomAttribute<IntegrationEventNameAttribute>()?.Name;

        Assert.That(name, Is.Not.Null.And.Match(VersionedName),
            $"{contract.Name} needs [IntegrationEventName(\"context.event-name.v1\")]; stored messages depend on it, not on the type name.");
    }

    private static IntegrationEventTypes CreateTypes()
    {
        var options = new IntegrationEventOptions();
        foreach (var assembly in ContractAssemblies)
        {
            options.ContractAssemblies.Add(assembly);
        }

        return new IntegrationEventTypes(Options.Create(options));
    }
}
