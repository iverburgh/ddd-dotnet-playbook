using System.Diagnostics.CodeAnalysis;

namespace BuildingBlocks.Domain;

/// <summary>
/// The outcome of an operation that can fail in an expected way. A failure carries exactly one typed
/// <see cref="Domain.Error"/>; unexpected failures are exceptions, not results.
/// </summary>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => Error is not null;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(Guard.NotNull(error));

    public static Result<T> Success<T>(T value) => new(value, null);

    public static Result<T> Failure<T>(Error error) => new(default, Guard.NotNull(error));
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, Error? error)
        : base(error) => _value = value;

    /// <summary>The value of a successful result. Reading it from a failure is a bug and throws.</summary>
    public T Value => IsFailure
        ? throw new InvalidOperationException($"A failed result has no value: {Error.Code}.")
        : _value!;
}
