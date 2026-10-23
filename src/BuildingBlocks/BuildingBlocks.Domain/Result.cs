namespace BuildingBlocks.Domain;

/// <summary>A business rule that said no. <see cref="Code"/> is stable and machine-readable; <see cref="Message"/> is for people.</summary>
public sealed record Error(string Code, string Message);

/// <summary>The outcome of a domain operation that can be refused by a business rule.</summary>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    public static Result<T> Success<T>(T value) => new(value, null);

    public static Result<T> Failure<T>(Error error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, Error? error)
        : base(error) => _value = value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"A failed result has no value: {Error!.Code}.");
}
