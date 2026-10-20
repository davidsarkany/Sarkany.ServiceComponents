using Orders.Api;
using Serilog;
using ServiceComponents.Application;
using ServiceComponents.Application.Mediator;
using ServiceComponents.Application.Senders;
using ServiceComponents.Core.Exceptions;

namespace Orders.Application;

public class PlaceOrderHandler : HandleCommand<PlaceOrder>
{
    private readonly IOrderStore _store;

    public PlaceOrderHandler(ILogger log, ICorrelation correlation, ISendQuery querySender, IPublishEvent eventPublisher, IOrderStore store)
        : base(log, correlation, querySender, eventPublisher)
    {
        _store = store;
    }

    public override async Task HandleAsync(PlaceOrder command, CancellationToken cancellationToken = default)
    {
        var order = new OrderDto {
            OrderId = command.OrderId,
            CustomerId = command.CustomerId,
            Product = command.Product,
            Quantity = command.Quantity,
            Status = OrderStatus.Placed
        };

        if (!_store.TryAdd(order)) {
            // BusinessException (and subclasses) are mapped to HTTP 400 with a "business" error body.
            throw new ConflictException($"Order {command.OrderId} already exists");
        }

        Log.Information("Order {orderId} placed by {customerId}", order.OrderId, order.CustomerId);
        await PublishAsync(new OrderPlaced(order.OrderId, order.CustomerId, order.Product, order.Quantity), cancellationToken);
    }
}

public class CancelOrderHandler : HandleCommand<CancelOrder>
{
    private readonly IOrderStore _store;

    public CancelOrderHandler(ILogger log, ICorrelation correlation, ISendQuery querySender, IPublishEvent eventPublisher, IOrderStore store)
        : base(log, correlation, querySender, eventPublisher)
    {
        _store = store;
    }

    public override async Task HandleAsync(CancelOrder command, CancellationToken cancellationToken = default)
    {
        // NotFoundException is mapped to HTTP 404.
        var order = _store.Find(command.OrderId) ?? throw new NotFoundException($"Order {command.OrderId} not found");
        if (order.Status == OrderStatus.Cancelled) {
            return;
        }

        order.Status = OrderStatus.Cancelled;
        await PublishAsync(new OrderCancelled(order.OrderId), cancellationToken);
    }
}

public class GetOrderHandler : HandleQuery<GetOrder, OrderDto>
{
    private readonly IOrderStore _store;

    public GetOrderHandler(ILogger log, ICorrelation correlation, ISendQuery querySender, IOrderStore store)
        : base(log, correlation, querySender)
    {
        _store = store;
    }

    public override Task<OrderDto> HandleAsync(GetOrder query, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Find(query.OrderId) ?? throw new NotFoundException($"Order {query.OrderId} not found"));
}
