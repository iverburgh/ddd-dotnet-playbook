using BuildingBlocks.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Shop.Api;

/// <summary>
/// Turns results and exceptions into HTTP. The mapping is by kind, not by case:
/// <list type="bullet">
/// <item>input that can never be valid (an invariant of a value object) is 400;</item>
/// <item>a business rule that said no is 422, with the rule's code;</item>
/// <item>an error code ending in <c>.not-found</c> is 404.</item>
/// </list>
/// </summary>
internal static class HttpResults
{
    public static IResult ToHttpResult(this Result result, Func<IResult> onSuccess) =>
        result.IsSuccess ? onSuccess() : Problem(result.Error!);

    public static IResult Problem(Error error) => TypedResults.Problem(
        title: error.Message,
        statusCode: error.Code.EndsWith(".not-found", StringComparison.Ordinal) ? StatusCodes.Status404NotFound : StatusCodes.Status422UnprocessableEntity,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}

internal sealed class InvariantViolationExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not InvariantViolationException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = exception.Message,
                Extensions = { ["code"] = "invalid-input" },
            },
        });
    }
}
