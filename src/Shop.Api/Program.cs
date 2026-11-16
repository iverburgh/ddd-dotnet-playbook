using BuildingBlocks.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Ordering.Infrastructure;
using Shipping.Infrastructure;
using Shop.Api;

var builder = WebApplication.CreateBuilder(args);

// The host composes the bounded contexts; each one only asks where its database is.
builder.Services.AddOrderingModule(db => db.UseSqlite(builder.Configuration.GetConnectionString("Ordering")));
builder.Services.AddShippingModule(db => db.UseSqlite(builder.Configuration.GetConnectionString("Shipping")));
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ExceptionToProblemDetailsHandler>();

// The handler logs what it handles; without this the middleware would log the same exception a second time.
builder.Services.Configure<ExceptionHandlerOptions>(options => options.SuppressDiagnosticsCallback = _ => true);

// A binding failure (malformed JSON, missing or null field) throws in every environment, so the exception handler
// answers it with the same problem details instead of an empty 400 outside Development.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

// Nullability at the boundary: a request that leaves out a constructor parameter or sends null for a
// non-nullable property is rejected while binding, before it can reach a handler as a half-built command.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});

var app = builder.Build();

app.UseExceptionHandler();

// Demo only: create the schema on start. Use migrations or a database project in a real system.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<ShippingDbContext>().Database.EnsureCreatedAsync();
}

app.MapOrderEndpoints();
app.MapShipmentEndpoints();

await app.RunAsync();

/// <summary>Exposed for <c>WebApplicationFactory</c> in the API tests.</summary>
public partial class Program;
