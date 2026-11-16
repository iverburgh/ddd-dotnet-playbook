using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace BuildingBlocks.Domain;

/// <summary>
/// Checks for programming errors: a caller in our own code that broke a contract. A failed guard is a bug and
/// ends as <c>500</c>; contrast <see cref="Invariant"/>, which rejects input that can never be valid
/// (<c>400</c>), and <see cref="Result"/>, which reports a business rule that said no.
/// </summary>
public static class Guard
{
    /// <summary>Returns <paramref name="value"/>, or throws <see cref="ArgumentNullException"/> when it is null.</summary>
    public static T NotNull<T>([NotNull] T? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where T : class =>
        value ?? throw new ArgumentNullException(name);

    /// <summary>Returns <paramref name="value"/>, or throws when it is null, empty or only white space.</summary>
    public static string NotNullOrWhiteSpace([NotNull] string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }
}
