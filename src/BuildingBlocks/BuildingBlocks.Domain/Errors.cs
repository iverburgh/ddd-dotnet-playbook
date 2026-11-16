using System.Text.RegularExpressions;

namespace BuildingBlocks.Domain;

/// <summary>
/// An expected failure: something the caller can be told about and act on. <see cref="Code"/> is stable and
/// machine-readable (clients, dashboards and dead-letter triage depend on it); <see cref="Message"/> is for people
/// and may change.
/// </summary>
/// <remarks>
/// The taxonomy is closed: every error is either a <see cref="ClientError"/> (the request or the state of the
/// aggregate is the problem; retrying the same thing gives the same answer) or a <see cref="ServerError"/> (we
/// could not do it right now). An error describes a <em>kind</em>, never an HTTP status: the API maps kinds to
/// statuses and a message handler maps the same kinds to settlement, so the domain stays free of transport.
/// </remarks>
public abstract partial record Error
{
    private protected Error(string code, string message)
    {
        if (code is null || !CodeFormat().IsMatch(code))
        {
            throw new ArgumentException(
                $"Error code '{code}' must be lower-case kebab-case segments separated by dots, for example 'order.not-draft'.",
                nameof(code));
        }

        Code = code;
        Message = Guard.NotNullOrWhiteSpace(message);
    }

    /// <summary>The stable code, for example <c>order.already-shipped</c>. Never rename a published code.</summary>
    public string Code { get; }

    /// <summary>A human-readable explanation of this occurrence.</summary>
    public string Message { get; }

    public override string ToString() => $"{Code}: {Message}";

    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodeFormat();
}

/// <summary>The request, or the state it meets, is the problem. The same request will fail the same way.</summary>
public abstract record ClientError : Error
{
    private protected ClientError(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>We could not do it. The request may well be fine.</summary>
public abstract record ServerError : Error
{
    private protected ServerError(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>
/// Marks an error that may succeed when tried again later, unchanged. A message handler abandons (retries) the
/// message instead of dead-lettering it; the API adds a <c>Retry-After</c> header.
/// </summary>
public interface IRetryableError
{
    /// <summary>A hint for when to try again, if the error knows one.</summary>
    TimeSpan? RetryAfter { get; }
}

/// <summary>The input is well-formed but not acceptable, for example a value outside its range.</summary>
public sealed record ValidationError : ClientError
{
    public ValidationError(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>The caller is known but may not do this.</summary>
public sealed record ForbiddenError : ClientError
{
    public ForbiddenError(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>The thing the request is about does not exist (or the caller may not know it exists).</summary>
public sealed record NotFoundError : ClientError
{
    public NotFoundError(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>The request clashes with the current state, for example a concurrent update or a duplicate.</summary>
public sealed record ConflictError : ClientError
{
    public ConflictError(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>A business rule said no: the request is understood and valid, but not allowed in this state.</summary>
public sealed record BusinessRuleError : ClientError
{
    public BusinessRuleError(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>The caller asked too often. It is the caller's doing, but waiting helps.</summary>
public sealed record RateLimitedError : ClientError, IRetryableError
{
    public RateLimitedError(string code, string message, TimeSpan? retryAfter = null)
        : base(code, message) => RetryAfter = retryAfter;

    public TimeSpan? RetryAfter { get; }
}

/// <summary>A dependency is unavailable or timed out. Trying again later, unchanged, may work.</summary>
public sealed record TransientError : ServerError, IRetryableError
{
    public TransientError(string code, string message, TimeSpan? retryAfter = null)
        : base(code, message) => RetryAfter = retryAfter;

    public TimeSpan? RetryAfter { get; }
}

/// <summary>
/// Something went wrong that nobody expected. Normally a thrown exception, not a returned error; this type exists
/// so the boundary that catches the exception can describe it with the same vocabulary.
/// </summary>
public sealed record UnexpectedError : ServerError
{
    public UnexpectedError(string code, string message)
        : base(code, message)
    {
    }
}
