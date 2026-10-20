using System.Collections.Concurrent;
using Orders.Api;

namespace Orders.Application;

/// <summary>Order storage. The sample keeps orders in memory; a real service would use a database.</summary>
public interface IOrderStore
{
    bool TryAdd(OrderDto order);
    OrderDto Find(string orderId);
}

public class InMemoryOrderStore : IOrderStore
{
    private readonly ConcurrentDictionary<string, OrderDto> _orders = new();

    public bool TryAdd(OrderDto order) => _orders.TryAdd(order.OrderId, order);

    public OrderDto Find(string orderId) => _orders.TryGetValue(orderId, out var order) ? order : null;
}
