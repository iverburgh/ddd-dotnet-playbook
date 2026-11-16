using BuildingBlocks.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Shop.Api;

/// <summary>
/// The last line of defence: every exception that escapes an endpoint ends here and becomes problem details in the
/// same shape as a returned error. Known exception types get their own kind; everything else is a bug and becomes a
/// <c>500</c> whose response says nothing about the cause. The cause goes to the log, correlated by trace id.
/// </summary>
/// <remarks>
/// The handler always handles, and logs itself, because a handled exception is not logged again by the
/// middleware (see <c>SuppressDiagnosticsCallback</c> in <c>Program.cs</c>). That gives one log entry per failure.
/// </remarks>
internal sealed partial class ExceptionToProblemDetailsHandler(ILogger<ExceptionToProblemDetailsHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer and nothing went wrong on our side.
            LogClientAborted(logger, httpContext.Request.Method, httpContext.Request.Path);
            return true;
        }

        var (error, status) = Classify(exception);
        await ErrorResponses.WriteAsync(httpContext, error, status, exception);
        return true;
    }

    /// <summary>Maps an exception to an error kind, and to a status when the framework already chose one.</summary>
    internal static (Error Error, int? Status) Classify(Exception exception) => exception switch
    {
        // A value object refused to exist: input that can never be valid. Its message is written for the caller.
        InvariantViolationException invariant => (new ValidationError("invalid-input", invariant.Message), null),

        // Binding failed: malformed JSON, a missing required field, a null for a non-nullable property. The
        // framework's message names internal types, so the caller gets a generic one.
        BadHttpRequestException badRequest => (
            new ValidationError("malformed-request", "The request could not be read. Check the JSON, the required fields and their types."),
            badRequest.StatusCode),

        // Optimistic concurrency: someone else saved first. The request may be fine on a fresh copy.
        DbUpdateConcurrencyException => (
            new ConflictError("concurrency-conflict", "The resource was changed by someone else. Reload it and try again."),
            null),

        _ => (new UnexpectedError("unexpected-error", "An exception escaped an endpoint."), null),
    };

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Method} {Path} was aborted by the client")]
    private static partial void LogClientAborted(ILogger logger, string method, string path);
}
