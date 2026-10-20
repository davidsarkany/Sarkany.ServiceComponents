using ServiceComponents.Api.Mediator;

namespace Orders.Api;

/// <summary>Returns a single order.</summary>
public class GetOrder : Query<OrderDto>
{
    public string OrderId { get; }

    public GetOrder(string orderId, string queryId = default) : base(queryId)
    {
        OrderId = orderId;
    }
}

public class OrderDto
{
    public string OrderId { get; set; }
    public string CustomerId { get; set; }
    public string Product { get; set; }
    public int Quantity { get; set; }
    public OrderStatus Status { get; set; }
}

public enum OrderStatus
{
    Placed,
    Cancelled
}
