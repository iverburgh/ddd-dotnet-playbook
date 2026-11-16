using System.Diagnostics;
using System.Globalization;
using BuildingBlocks.Domain;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Shop.Api;

/// <summary>
/// Turns results into HTTP. The mapping is by error <em>kind</em> (its type), never by parsing the code, and it
/// lives here once for every bounded context. See <see cref="ErrorHttpMapping"/> for the table.
/// </summary>
internal static class HttpResults
{
    public static IResult ToHttpResult(this Result result, Func<IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        return result.IsSuccess ? onSuccess() : Problem(result.Error);
    }

    public static IResult Problem(Error error) => new ErrorResult(Guard.NotNull(error));
}

/// <summary>An <see cref="IResult"/> that writes a typed error as RFC 9457 problem details.</summary>
internal sealed class ErrorResult(Error error) : IResult, IStatusCodeHttpResult, IContentTypeHttpResult
{
    public Error Error { get; } = error;

    public int? StatusCode => ErrorHttpMapping.For(Error).Status;

    public string ContentType => ErrorResponses.ProblemJson;

    public Task ExecuteAsync(HttpContext httpContext) => ErrorResponses.WriteAsync(httpContext, Error);
}

/// <summary>
/// The one table from error kind to HTTP. <see cref="Type"/> and <see cref="Title"/> describe the kind and never
/// change per occurrence (RFC 9457 §3.1); the occurrence goes in <c>detail</c> and the specific problem in the
/// <c>code</c> extension.
/// </summary>
internal sealed record ErrorHttpMapping(int Status, string Type, string Title)
{
    private const string Rfc9110 = "https://www.rfc-editor.org/rfc/rfc9110#section-";

    private static readonly ErrorHttpMapping Validation = new(StatusCodes.Status400BadRequest, Rfc9110 + "15.5.1", "The request is invalid.");
    private static readonly ErrorHttpMapping Forbidden = new(StatusCodes.Status403Forbidden, Rfc9110 + "15.5.4", "The request is not allowed.");
    private static readonly ErrorHttpMapping NotFound = new(StatusCodes.Status404NotFound, Rfc9110 + "15.5.5", "The resource was not found.");
    private static readonly ErrorHttpMapping Conflict = new(StatusCodes.Status409Conflict, Rfc9110 + "15.5.10", "The request conflicts with the current state.");
    private static readonly ErrorHttpMapping BusinessRule = new(StatusCodes.Status422UnprocessableEntity, Rfc9110 + "15.5.21", "A business rule rejected the request.");
    private static readonly ErrorHttpMapping RateLimited = new(StatusCodes.Status429TooManyRequests, "https://www.rfc-editor.org/rfc/rfc6585#section-4", "Too many requests.");
    private static readonly ErrorHttpMapping Transient = new(StatusCodes.Status503ServiceUnavailable, Rfc9110 + "15.6.4", "The service is temporarily unavailable.");
    private static readonly ErrorHttpMapping Unexpected = new(StatusCodes.Status500InternalServerError, Rfc9110 + "15.6.1", "An unexpected error occurred.");

    public static ErrorHttpMapping For(Error error) => error switch
    {
        ValidationError => Validation,
        ForbiddenError => Forbidden,
        NotFoundError => NotFound,
        ConflictError => Conflict,
        BusinessRuleError => BusinessRule,
        RateLimitedError => RateLimited,
        TransientError => Transient,
        UnexpectedError => Unexpected,

        // The taxonomy is closed (private protected constructors), so a new kind is a change in this solution.
        // Failing loudly beats silently answering 400 or 500 for it; a test covers every kind.
        _ => throw new UnreachableException($"No HTTP mapping for error type {error.GetType().Name}."),
    };
}

/// <summary>Writes an error response. Used by <see cref="ErrorResult"/> and by the exception handler.</summary>
internal static partial class ErrorResponses
{
    public const string ProblemJson = "application/problem+json";

    /// <summary>The detail of every server error. The real message is in the log, found by the trace id.</summary>
    public const string ServerErrorDetail = "The error has been logged. Quote the traceId when you report it.";

    private const string LoggerCategory = "Shop.Api.Errors";

    /// <param name="httpContext">The request.</param>
    /// <param name="error">The error to describe.</param>
    /// <param name="statusOverride">A status the framework already decided, for example <c>413</c> for a body that is too large.</param>
    /// <param name="exception">The exception behind the error, for the log only; never written to the response.</param>
    public static async Task WriteAsync(HttpContext httpContext, Error error, int? statusOverride = null, Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(error);

        var mapping = ErrorHttpMapping.For(error);
        var status = statusOverride ?? mapping.Status;
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        Log(httpContext, error, status, exception);

        var response = httpContext.Response;
        response.StatusCode = status;
        if (error is IRetryableError { RetryAfter: { } retryAfter })
        {
            response.Headers.RetryAfter = ((long)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        // When the framework chose another status, leave type and title empty so the defaults for that status apply.
        var matchesKind = status == mapping.Status;
        var problem = new ProblemDetails
        {
            Status = status,
            Type = matchesKind ? mapping.Type : null,
            Title = matchesKind ? mapping.Title : null,
            Detail = error is ClientError ? error.Message : ServerErrorDetail,
            Instance = httpContext.Request.Path,
            Extensions =
            {
                ["code"] = error.Code,
                ["traceId"] = traceId,
            },
        };

        var problemDetailsService = httpContext.RequestServices.GetService<IProblemDetailsService>();
        var written = problemDetailsService is not null && await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });

        if (!written)
        {
            await response.WriteAsJsonAsync(problem, options: null, contentType: ProblemJson, httpContext.RequestAborted);
        }
    }

    private static void Log(HttpContext httpContext, Error error, int status, Exception? exception)
    {
        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
        var request = httpContext.Request;
        if (error is ServerError)
        {
            LogServerError(logger, exception, request.Method, request.Path, status, error.Code, error.Message);
        }
        else
        {
            LogClientError(logger, exception, request.Method, request.Path, status, error.Code);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Method} {Path} failed with {Status} {ErrorCode}: {ErrorMessage}")]
    private static partial void LogServerError(ILogger logger, Exception? exception, string method, string path, int status, string errorCode, string errorMessage);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Method} {Path} was refused with {Status} {ErrorCode}")]
    private static partial void LogClientError(ILogger logger, Exception? exception, string method, string path, int status, string errorCode);
}
