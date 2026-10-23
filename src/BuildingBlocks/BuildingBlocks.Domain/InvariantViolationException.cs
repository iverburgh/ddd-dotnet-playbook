namespace BuildingBlocks.Domain;

/// <summary>
/// Thrown when code tries to create a value or state that can never exist, such as a negative amount
/// of money. This is a programming or input error, not a business outcome: business rules that can
/// legitimately say "no" return a <see cref="Result"/> instead.
/// </summary>
public sealed class InvariantViolationException(string message) : Exception(message);
