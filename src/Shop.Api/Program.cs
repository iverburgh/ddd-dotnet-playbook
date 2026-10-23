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
builder.Services.AddExceptionHandler<InvariantViolationExceptionHandler>();

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
