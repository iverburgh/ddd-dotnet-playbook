using System.Reflection;
using BuildingBlocks.Domain;
using Ordering.Domain.Orders;

namespace Architecture.Tests;

/// <summary>
/// Error codes are a public contract: clients branch on them, dashboards count them, and dead-letter queues are
/// triaged by them. This approved list makes renaming a code, or changing its kind (and so its HTTP status and its
/// settlement), a visible change in review instead of an accident. A new error cannot be added without adding it here.
/// </summary>
public sealed class ErrorCatalogueTests
{
    private static readonly Assembly[] Assemblies =
    [
        typeof(Order).Assembly,
        typeof(Ordering.Application.PlaceOrderHandler).Assembly,
        typeof(Shipping.Domain.Shipments.Shipment).Assembly,
        typeof(Shipping.Application.DispatchShipmentHandler).Assembly,
    ];

    private static readonly Dictionary<string, (string Code, Type Kind)> Approved = new()
    {
        ["OrderErrors.Empty"] = ("order.empty", typeof(BusinessRuleError)),
        ["OrderErrors.TooManyLines"] = ("order.too-many-lines", typeof(BusinessRuleError)),
        ["OrderErrors.AlreadyCancelled"] = ("order.already-cancelled", typeof(BusinessRuleError)),
        ["OrderErrors.AlreadyShipped"] = ("order.already-shipped", typeof(BusinessRuleError)),
        ["OrderErrors.NotDraft"] = ("order.not-draft", typeof(BusinessRuleError)),
        ["OrderErrors.NotPlaced"] = ("order.not-placed", typeof(BusinessRuleError)),
        ["OrderErrors.CurrencyMismatch"] = ("order.currency-mismatch", typeof(BusinessRuleError)),
        ["OrderErrors.PriceDiffers"] = ("order.price-differs", typeof(BusinessRuleError)),
        ["OrderErrors.QuantityTooLarge"] = ("order.quantity-too-large", typeof(BusinessRuleError)),
        ["OrderErrors.LineNotFound"] = ("order.line-not-found", typeof(NotFoundError)),
        ["OrderingErrors.OrderNotFound"] = ("order.not-found", typeof(NotFoundError)),
        ["ShipmentErrors.AlreadyDispatched"] = ("shipment.already-dispatched", typeof(BusinessRuleError)),
        ["ShipmentErrors.AlreadyCancelled"] = ("shipment.already-cancelled", typeof(BusinessRuleError)),
        ["ShippingErrors.ShipmentNotFound"] = ("shipment.not-found", typeof(NotFoundError)),
    };

    [Test]
    public void Every_error_in_the_code_is_in_the_approved_catalogue_with_its_code_and_kind()
    {
        var actual = DiscoverErrors().ToDictionary(e => e.Name, e => (e.Error.Code, Kind: e.Error.GetType()));

        Assert.Multiple(() =>
        {
            Assert.That(actual.Keys, Is.EquivalentTo(Approved.Keys), "Add new errors to the catalogue; never remove a published one silently.");
            foreach (var (name, expected) in Approved.Where(a => actual.ContainsKey(a.Key)))
            {
                Assert.That(actual[name], Is.EqualTo(expected), name);
            }
        });
    }

    [Test]
    public void Error_codes_are_unique()
    {
        var duplicates = Approved.Values.GroupBy(v => v.Code).Where(g => g.Count() > 1).Select(g => g.Key);

        Assert.That(duplicates, Is.Empty);
    }

    [Test]
    public void Every_error_member_declares_its_concrete_kind()
    {
        // `public static NotFoundError OrderNotFound(...)` documents the kind at the declaration and lets the
        // compiler check it; `public static Error ...` hides it until run time.
        var vague = ErrorMembers()
            .Where(m => m.DeclaredType.IsAbstract)
            .Select(m => m.Name);

        Assert.That(vague, Is.Empty);
    }

    private static IEnumerable<(string Name, Type DeclaredType, MemberInfo Member)> ErrorMembers()
    {
        var catalogues = Assemblies
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => t is { IsAbstract: true, IsSealed: true } && t.Name.EndsWith("Errors", StringComparison.Ordinal));

        foreach (var catalogue in catalogues)
        {
            foreach (var field in catalogue.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => typeof(Error).IsAssignableFrom(f.FieldType)))
            {
                yield return ($"{catalogue.Name}.{field.Name}", field.FieldType, field);
            }

            foreach (var method in catalogue.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => typeof(Error).IsAssignableFrom(m.ReturnType)))
            {
                yield return ($"{catalogue.Name}.{method.Name}", method.ReturnType, method);
            }
        }
    }

    private static IEnumerable<(string Name, Error Error)> DiscoverErrors() =>
        ErrorMembers().Select(m => (m.Name, m.Member switch
        {
            FieldInfo field => (Error)field.GetValue(null)!,
            MethodInfo method => (Error)method.Invoke(null, method.GetParameters().Select(p => SampleArgument(p.ParameterType)).ToArray())!,
            _ => throw new InvalidOperationException(m.Name),
        }));

    private static object SampleArgument(Type type) =>
        type == typeof(string) ? "EUR"
        : type == typeof(Guid) ? Guid.NewGuid()
        : type == typeof(OrderStatus) ? OrderStatus.Placed
        : type == typeof(ProductCode) ? new ProductCode("BOOK-1")
        : throw new InvalidOperationException($"Add a sample value for {type.Name} to {nameof(ErrorCatalogueTests)}.");
}
