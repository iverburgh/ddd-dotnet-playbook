using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application;

namespace Shop.Api.Tests;

/// <summary>
/// Failures through the real pipeline: exceptions that escape an endpoint, requests that cannot be bound, and a
/// not-found that is not a business rule. Every one ends as problem details in the same shape.
/// </summary>
public sealed class ErrorHandlingFlowTests
{
    private readonly ThrowingOrderQueries _queries = new();
    private string _directory = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "shop-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:Ordering", $"Data Source={Path.Combine(_directory, "ordering.db")}");
            host.UseSetting("ConnectionStrings:Shipping", $"Data Source={Path.Combine(_directory, "shipping.db")}");
            host.ConfigureTestServices(services => services.AddSingleton<IOrderQueries>(_queries));
        });
        _client = _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public async Task An_unexpected_exception_is_a_500_that_reveals_nothing_about_its_cause()
    {
        _queries.Throw = new InvalidOperationException("Connection string Server=db-primary;Password=hunter2 is wrong.");

        var response = await _client.GetAsync($"/orders/{Guid.NewGuid()}");

        var problem = await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "unexpected-error");
        Assert.Multiple(() =>
        {
            Assert.That(problem.GetProperty("title").GetString(), Is.EqualTo("An unexpected error occurred."));
            Assert.That(problem.GetProperty("detail").GetString(), Is.EqualTo(ErrorResponses.ServerErrorDetail));
            Assert.That(problem.GetProperty("traceId").GetString(), Is.Not.Empty);
            Assert.That(problem.GetRawText(), Does.Not.Contain("hunter2").And.Not.Contain("InvalidOperationException").And.Not.Contain("   at "));
        });
    }

    [Test]
    public async Task An_optimistic_concurrency_failure_is_a_409()
    {
        _queries.Throw = new DbUpdateConcurrencyException("The row was changed.");

        var response = await _client.GetAsync($"/orders/{Guid.NewGuid()}");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrency-conflict");
    }

    [TestCase("{ \"customerId\": ", TestName = "Malformed JSON is a 400")]
    [TestCase("{ \"customerId\": \"<id>\" }", TestName = "A missing required field is a 400")]
    [TestCase("{ \"customerId\": \"<id>\", \"currency\": null }", TestName = "A null for a non-nullable field is a 400")]
    public async Task A_request_that_cannot_be_bound_is_a_400_before_it_reaches_a_handler(string json)
    {
        var body = json.Replace("<id>", Guid.NewGuid().ToString(), StringComparison.Ordinal);

        var response = await _client.PostAsync("/orders", new StringContent(body, Encoding.UTF8, "application/json"));

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "malformed-request");
        Assert.That(problem.GetRawText(), Does.Not.Contain("StartOrderRequest"), "Framework messages name internal types; they stay in the log.");
    }

    [Test]
    public async Task Removing_a_line_that_is_not_on_the_order_is_a_404_not_a_422()
    {
        var start = await _client.PostAsJsonAsync("/orders", new { customerId = Guid.NewGuid(), currency = "EUR" });
        var orderId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await _client.DeleteAsync($"/orders/{orderId}/lines/BOOK-1984");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "order.line-not-found");
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.That(response.StatusCode, Is.EqualTo(status));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo(code));
        return problem;
    }

    private sealed class ThrowingOrderQueries : IOrderQueries
    {
        public Exception? Throw { get; set; }

        public Task<OrderDetails?> GetAsync(Guid orderId, CancellationToken cancellationToken) =>
            Throw is null ? Task.FromResult<OrderDetails?>(null) : Task.FromException<OrderDetails?>(Throw);
    }
}
