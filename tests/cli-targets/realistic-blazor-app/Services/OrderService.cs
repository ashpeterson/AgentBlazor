public sealed record OrderSummary(string OrderId, string Status, DateOnly RequiredBy);

public sealed class OrderService
{
    public Task<IReadOnlyList<OrderSummary>> FindDelayedOrdersAsync(DateOnly since)
        => Task.FromResult<IReadOnlyList<OrderSummary>>([]);

    public Task<string> DraftCustomerUpdateAsync(string orderId)
        => Task.FromResult($"Draft update for {orderId}");

    public Task<bool> ReleaseHoldAsync(string orderId)
        => Task.FromResult(true);
}
