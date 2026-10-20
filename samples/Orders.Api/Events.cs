using ServiceComponents.Api.Mediator;

namespace Orders.Api;

public class OrderPlaced : Event
{
    public string OrderId { get; }
    public string CustomerId { get; }
    public string Product { get; }
    public int Quantity { get; }

    public OrderPlaced(string orderId, string customerId, string product, int quantity, string eventId = default)
        : base(eventId)
    {
        OrderId = orderId;
        CustomerId = customerId;
        Product = product;
        Quantity = quantity;
    }
}

public class OrderCancelled : Event
{
    public string OrderId { get; }

    public OrderCancelled(string orderId, string eventId = default) : base(eventId)
    {
        OrderId = orderId;
    }
}
