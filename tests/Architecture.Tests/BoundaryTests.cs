using System.Reflection;
using System.Runtime.CompilerServices;
using BuildingBlocks.Domain;

namespace Architecture.Tests;

/// <summary>
/// The layer and context boundaries, checked on every build. A boundary that is only written down
/// erodes; a failing test does not. These check the assemblies each assembly actually uses.
/// </summary>
public sealed class BoundaryTests
{
    private static readonly Assembly[] All =
    [
        typeof(Entity<>).Assembly,                                              // BuildingBlocks.Domain
        typeof(BuildingBlocks.Contracts.IIntegrationEvent).Assembly,
        typeof(BuildingBlocks.Application.IOutbox).Assembly,
        typeof(BuildingBlocks.Infrastructure.ServiceCollectionExtensions).Assembly,
        typeof(Ordering.Domain.Orders.Order).Assembly,
        typeof(Ordering.Contracts.OrderPlacedIntegrationEvent).Assembly,
        typeof(Ordering.Application.PlaceOrderHandler).Assembly,
        typeof(Ordering.Infrastructure.OrderingDbContext).Assembly,
        typeof(Shipping.Domain.Shipments.Shipment).Assembly,
        typeof(Shipping.Contracts.ShipmentDispatchedIntegrationEvent).Assembly,
        typeof(Shipping.Application.DispatchShipmentHandler).Assembly,
        typeof(Shipping.Infrastructure.ShippingDbContext).Assembly,
    ];

    private static readonly string[] BoundedContexts = ["Ordering", "Shipping"];

    private static IEnumerable<Assembly> Layer(string layer) => All.Where(a => a.GetName().Name!.EndsWith($".{layer}", StringComparison.Ordinal));

    private static IEnumerable<string> References(Assembly assembly) => assembly.GetReferencedAssemblies().Select(r => r.Name!);

    private static bool IsFramework(string name) => name.StartsWith("System", StringComparison.Ordinal) || name is "netstandard" or "mscorlib";

    [TestCaseSource(nameof(DomainAssemblies))]
    public void The_domain_depends_on_nothing_but_the_framework_and_the_domain_building_blocks(Assembly domain)
    {
        var offending = References(domain).Where(r => !IsFramework(r) && r != "BuildingBlocks.Domain");

        Assert.That(offending, Is.Empty, $"{domain.GetName().Name} must stay free of persistence, messaging and web concerns.");
    }

    [TestCaseSource(nameof(ApplicationAssemblies))]
    public void The_application_layer_knows_no_infrastructure(Assembly application)
    {
        var offending = References(application).Where(r =>
            r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
            r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
            r.EndsWith(".Infrastructure", StringComparison.Ordinal));

        Assert.That(offending, Is.Empty);
    }

    [TestCaseSource(nameof(ContractAssemblies))]
    public void Contracts_depend_on_nothing_but_the_contract_building_block(Assembly contracts)
    {
        var offending = References(contracts).Where(r => !IsFramework(r) && r != "BuildingBlocks.Contracts");

        Assert.That(offending, Is.Empty, "Contracts are shared with other contexts; they must not drag a domain along.");
    }

    [TestCaseSource(nameof(ContextPairs))]
    public void A_bounded_context_only_sees_the_contracts_of_another(string context, string other)
    {
        var offending = All
            .Where(a => a.GetName().Name!.StartsWith($"{context}.", StringComparison.Ordinal))
            .SelectMany(a => References(a).Select(r => (Assembly: a.GetName().Name, Reference: r)))
            .Where(x => x.Reference.StartsWith($"{other}.", StringComparison.Ordinal) && x.Reference != $"{other}.Contracts")
            .Select(x => $"{x.Assembly} -> {x.Reference}");

        Assert.That(offending, Is.Empty);
    }

    [TestCaseSource(nameof(DomainAssemblies))]
    public void Entities_cannot_be_changed_from_outside_through_public_setters(Assembly domain)
    {
        var entityTypes = domain.GetTypes().Where(t => IsSubclassOfGeneric(t, typeof(Entity<>)));
        var publicSetters = entityTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is { IsPublic: true } && !IsInitOnly(p))
                .Select(p => $"{t.Name}.{p.Name}"));

        Assert.That(publicSetters, Is.Empty, "State changes go through behaviour methods that guard the invariants.");
    }

    private static IEnumerable<Assembly> DomainAssemblies() => Layer("Domain");

    private static IEnumerable<Assembly> ApplicationAssemblies() => Layer("Application");

    private static IEnumerable<Assembly> ContractAssemblies() => Layer("Contracts");

    private static IEnumerable<TestCaseData> ContextPairs() =>
        from context in BoundedContexts
        from other in BoundedContexts
        where context != other
        select new TestCaseData(context, other);

    private static bool IsSubclassOfGeneric(Type? type, Type generic)
    {
        for (type = type?.BaseType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == generic)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInitOnly(PropertyInfo property) =>
        property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
}
