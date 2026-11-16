using System.Reflection;
using System.Text.Json;
using BuildingBlocks.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.Api.Tests;

/// <summary>
/// The table from error kind to HTTP, without a server: each kind gets its status, a stable type and title, the
/// right detail, and the <c>code</c> and <c>traceId</c> extensions.
/// </summary>
public sealed class ProblemDetailsMappingTests
{
    private const string Rfc9110 = "https://www.rfc-editor.org/rfc/rfc9110#section-";

    private static IEnumerable<TestCaseData> Kinds()
    {
        yield return Case(new ValidationError("order.invalid", "Invalid."), 400, Rfc9110 + "15.5.1", "The request is invalid.");
        yield return Case(new ForbiddenError("order.forbidden", "Not yours."), 403, Rfc9110 + "15.5.4", "The request is not allowed.");
        yield return Case(new NotFoundError("order.not-found", "Gone."), 404, Rfc9110 + "15.5.5", "The resource was not found.");
        yield return Case(new ConflictError("order.conflict", "Changed."), 409, Rfc9110 + "15.5.10", "The request conflicts with the current state.");
        yield return Case(new BusinessRuleError("order.already-shipped", "Too late."), 422, Rfc9110 + "15.5.21", "A business rule rejected the request.");
        yield return Case(new RateLimitedError("order.rate-limited", "Slow down."), 429, "https://www.rfc-editor.org/rfc/rfc6585#section-4", "Too many requests.");
        yield return Case(new TransientError("payment.unavailable", "Provider down."), 503, Rfc9110 + "15.6.4", "The service is temporarily unavailable.");
        yield return Case(new UnexpectedError("unexpected-error", "Bug."), 500, Rfc9110 + "15.6.1", "An unexpected error occurred.");

        static TestCaseData Case(Error error, int status, string type, string title) =>
            new TestCaseData(error, status, type, title).SetArgDisplayNames(error.GetType().Name);
    }

    [TestCaseSource(nameof(Kinds))]
    public async Task Each_kind_has_its_status_type_title_code_and_trace_id(Error error, int status, string type, string title)
    {
        var (context, problem) = await ExecuteAsync(HttpResults.Problem(error));

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(status));
            Assert.That(context.Response.ContentType, Does.StartWith("application/problem+json"));
            Assert.That(problem.GetProperty("status").GetInt32(), Is.EqualTo(status));
            Assert.That(problem.GetProperty("type").GetString(), Is.EqualTo(type));
            Assert.That(problem.GetProperty("title").GetString(), Is.EqualTo(title));
            Assert.That(problem.GetProperty("instance").GetString(), Is.EqualTo("/orders/42"));
            Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo(error.Code));
            Assert.That(problem.GetProperty("traceId").GetString(), Is.EqualTo("test-trace"));
        });
    }

    [Test]
    public async Task A_client_error_explains_itself_in_the_detail()
    {
        var (_, problem) = await ExecuteAsync(HttpResults.Problem(new BusinessRuleError("order.already-shipped", "A shipped order can no longer be cancelled.")));

        Assert.That(problem.GetProperty("detail").GetString(), Is.EqualTo("A shipped order can no longer be cancelled."));
    }

    [Test]
    public async Task A_server_error_never_shows_its_message()
    {
        var (_, problem) = await ExecuteAsync(HttpResults.Problem(new TransientError("payment.unavailable", "Connection to db-primary:1433 refused.")));

        Assert.Multiple(() =>
        {
            Assert.That(problem.GetProperty("detail").GetString(), Is.EqualTo(ErrorResponses.ServerErrorDetail));
            Assert.That(problem.GetRawText(), Does.Not.Contain("db-primary"));
        });
    }

    [Test]
    public async Task A_retryable_error_with_a_hint_sets_retry_after_in_whole_seconds_rounded_up()
    {
        var (context, _) = await ExecuteAsync(HttpResults.Problem(new TransientError("payment.unavailable", "Down.", TimeSpan.FromSeconds(1.2))));

        Assert.That(context.Response.Headers.RetryAfter.ToString(), Is.EqualTo("2"));
    }

    [Test]
    public async Task A_retryable_error_without_a_hint_sets_no_retry_after()
    {
        var (context, _) = await ExecuteAsync(HttpResults.Problem(new RateLimitedError("order.rate-limited", "Slow down.")));

        Assert.That(context.Response.Headers.ContainsKey("Retry-After"), Is.False);
    }

    [Test]
    public void Every_error_kind_has_an_http_mapping()
    {
        var kinds = typeof(Error).Assembly.GetTypes().Where(t => t is { IsAbstract: false } && t.IsSubclassOf(typeof(Error)));

        Assert.Multiple(() =>
        {
            foreach (var kind in kinds)
            {
                var error = Instantiate(kind);
                Assert.That(() => ErrorHttpMapping.For(error), Throws.Nothing, kind.Name);
                Assert.That(ErrorHttpMapping.For(error).Status >= 500, Is.EqualTo(error is ServerError), $"{kind.Name}: 5xx if and only if it is a server error");
            }
        });
    }

    private static Error Instantiate(Type kind)
    {
        var constructor = kind.GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select((p, i) => i switch { 0 => "sample.code", 1 => "Sample message.", _ => p.DefaultValue })
            .ToArray();
        return (Error)constructor.Invoke(arguments);
    }

    private static async Task<(HttpContext Context, JsonElement Problem)> ExecuteAsync(IResult result)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "test-trace" };
        context.Request.Path = "/orders/42";
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        return (context, await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body));
    }
}
