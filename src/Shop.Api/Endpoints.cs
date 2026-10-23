using Ordering.Application;
using Shipping.Application;

namespace Shop.Api;

/// <summary>Endpoints are thin: translate HTTP to a command, call the handler, translate the result back.</summary>
internal static class Endpoints
{
    public sealed record StartOrderRequest(Guid CustomerId, string Currency);

    public sealed record AddLineRequest(string ProductCode, int Quantity, decimal UnitPrice, string Currency);

    public sealed record PlaceOrderRequest(string Street, string PostalCode, string City, string CountryCode);

    public sealed record CancelOrderRequest(string Reason);

    public sealed record DispatchRequest(string TrackingCode);

    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders");

        orders.MapPost("/", async (StartOrderRequest request, StartOrderHandler handler, CancellationToken ct) =>
        {
            var id = await handler.Handle(new StartOrder(request.CustomerId, request.Currency), ct);
            return Results.Created($"/orders/{id}", new { id });
        });

        orders.MapGet("/{id:guid}", async (Guid id, IOrderQueries queries, CancellationToken ct) =>
            await queries.GetAsync(id, ct) is { } order ? Results.Ok(order) : HttpResults.Problem(OrderingErrors.OrderNotFound(id)));

        orders.MapPost("/{id:guid}/lines", async (Guid id, AddLineRequest request, AddOrderLineHandler handler, CancellationToken ct) =>
            (await handler.Handle(new AddOrderLine(id, request.ProductCode, request.Quantity, request.UnitPrice, request.Currency), ct))
                .ToHttpResult(Results.NoContent));

        orders.MapDelete("/{id:guid}/lines/{productCode}", async (Guid id, string productCode, RemoveOrderLineHandler handler, CancellationToken ct) =>
            (await handler.Handle(new RemoveOrderLine(id, productCode), ct)).ToHttpResult(Results.NoContent));

        orders.MapPost("/{id:guid}/place", async (Guid id, PlaceOrderRequest request, PlaceOrderHandler handler, CancellationToken ct) =>
            (await handler.Handle(new PlaceOrder(id, request.Street, request.PostalCode, request.City, request.CountryCode), ct))
                .ToHttpResult(Results.NoContent));

        orders.MapPost("/{id:guid}/cancel", async (Guid id, CancelOrderRequest request, CancelOrderHandler handler, CancellationToken ct) =>
            (await handler.Handle(new CancelOrder(id, request.Reason), ct)).ToHttpResult(Results.NoContent));
    }

    public static void MapShipmentEndpoints(this IEndpointRouteBuilder app)
    {
        var shipments = app.MapGroup("/shipments");

        shipments.MapGet("/", async (Guid orderId, IShipmentQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetByOrderAsync(orderId, ct)));

        shipments.MapPost("/{id:guid}/dispatch", async (Guid id, DispatchRequest request, DispatchShipmentHandler handler, CancellationToken ct) =>
            (await handler.Handle(new DispatchShipment(id, request.TrackingCode), ct)).ToHttpResult(Results.NoContent));
    }
}
