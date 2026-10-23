using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Shop.Api.Tests;

/// <summary>
/// The whole system through HTTP: both bounded contexts, real SQLite files, the outbox publishers
/// running in the background. Assertions about the other context poll, because it is eventually
/// consistent by design.
/// </summary>
public sealed class OrderFlowTests
{
    private static readonly TimeSpan EventualTimeout = TimeSpan.FromSeconds(15);

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
            host.UseSetting("Outbox:PollingInterval", "00:00:00.200");
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
    public async Task A_placed_order_is_shipped_and_can_then_no_longer_be_cancelled()
    {
        var orderId = await PlaceOrderAsync();

        var shipment = await EventuallyAsync(
            async () => (await GetJsonAsync($"/shipments?orderId={orderId}")).EnumerateArray().ToList(),
            shipments => shipments.Count == 1);
        Assert.That(shipment[0].GetProperty("status").GetString(), Is.EqualTo("Pending"));
        Assert.That(shipment[0].GetProperty("items")[0].GetProperty("productCode").GetString(), Is.EqualTo("BOOK-1984"));

        var shipmentId = shipment[0].GetProperty("id").GetGuid();
        var dispatch = await _client.PostAsJsonAsync($"/shipments/{shipmentId}/dispatch", new { trackingCode = "TRACK12345NL" });
        Assert.That(dispatch.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var order = await EventuallyAsync(() => GetJsonAsync($"/orders/{orderId}"), o => o.GetProperty("status").GetString() == "Shipped");
        Assert.That(order.GetProperty("trackingCode").GetString(), Is.EqualTo("TRACK12345NL"));

        var cancel = await _client.PostAsJsonAsync($"/orders/{orderId}/cancel", new { reason = "Changed my mind" });
        await AssertProblemAsync(cancel, HttpStatusCode.UnprocessableEntity, "order.already-shipped");
    }

    [Test]
    public async Task Cancelling_a_placed_order_cancels_its_shipment()
    {
        var orderId = await PlaceOrderAsync();
        await EventuallyAsync(async () => (await GetJsonAsync($"/shipments?orderId={orderId}")).GetArrayLength(), count => count == 1);

        var cancel = await _client.PostAsJsonAsync($"/orders/{orderId}/cancel", new { reason = "Changed my mind" });
        Assert.That(cancel.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        await EventuallyAsync(
            async () => (await GetJsonAsync($"/shipments?orderId={orderId}"))[0].GetProperty("status").GetString(),
            status => status == "Cancelled");
    }

    [Test]
    public async Task A_broken_business_rule_is_a_422_with_its_code()
    {
        var orderId = await StartOrderAsync();

        var response = await _client.PostAsJsonAsync($"/orders/{orderId}/lines", new { productCode = "PEN-BLUE", quantity = 1, unitPrice = 2.00m, currency = "USD" });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "order.currency-mismatch");
    }

    [Test]
    public async Task Input_that_can_never_be_valid_is_a_400()
    {
        var orderId = await StartOrderAsync();

        var response = await _client.PostAsJsonAsync($"/orders/{orderId}/lines", new { productCode = "PEN-BLUE", quantity = 0, unitPrice = 2.00m, currency = "EUR" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid-input");
    }

    [Test]
    public async Task An_unknown_order_is_a_404()
    {
        var response = await _client.PostAsJsonAsync($"/orders/{Guid.NewGuid()}/cancel", new { reason = "Whatever" });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "order.not-found");
    }

    private async Task<Guid> StartOrderAsync()
    {
        var response = await _client.PostAsJsonAsync("/orders", new { customerId = Guid.NewGuid(), currency = "EUR" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> PlaceOrderAsync()
    {
        var orderId = await StartOrderAsync();

        var line = await _client.PostAsJsonAsync($"/orders/{orderId}/lines", new { productCode = "BOOK-1984", quantity = 2, unitPrice = 12.50m, currency = "EUR" });
        Assert.That(line.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var place = await _client.PostAsJsonAsync($"/orders/{orderId}/place", new { street = "Main Street 1", postalCode = "1234 AB", city = "Springfield", countryCode = "NL" });
        Assert.That(place.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        return orderId;
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var deadline = DateTime.UtcNow + EventualTimeout;
        while (true)
        {
            var value = await read();
            if (done(value))
            {
                return value;
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Condition not reached within {EventualTimeout}; last value: {value}");
            }

            await Task.Delay(100);
        }
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.That(response.StatusCode, Is.EqualTo(status));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo(code));
    }
}
