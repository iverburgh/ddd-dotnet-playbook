using System.Diagnostics.CodeAnalysis;

namespace BuildingBlocks.Domain;

/// <summary>Guards for values and states that can never exist. A failed guard is a bug or bad input, never a business outcome.</summary>
public static class Invariant
{
    public static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvariantViolationException(message);
        }
    }
}
