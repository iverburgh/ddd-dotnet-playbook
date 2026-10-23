namespace Ordering.Application;

/// <summary>The read side: shapes made for the caller, not for the business rules.</summary>
public sealed record OrderDetails(
    Guid Id,
    Guid CustomerId,
    string Status,
    string Currency,
    decimal Total,
    IReadOnlyList<OrderLineDetails> Lines,
    string? TrackingCode,
    string? CancellationReason,
    int Version);

public sealed record OrderLineDetails(string ProductCode, int Quantity, decimal UnitPrice, decimal LineTotal);

public interface IOrderQueries
{
    Task<OrderDetails?> GetAsync(Guid orderId, CancellationToken cancellationToken);
}
