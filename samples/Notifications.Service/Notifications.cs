using System.Collections.Concurrent;
using Orders.Api;
using ILogger = Serilog.ILogger;
using ServiceComponents.Application;
using ServiceComponents.Application.Mediator;
using ServiceComponents.Application.Senders;

namespace Notifications.Service;

public record Notification(string OrderId, string Message, string CorrelationId);

public interface INotificationLog
{
    void Add(Notification notification);
    IReadOnlyCollection<Notification> All { get; }
}

public class InMemoryNotificationLog : INotificationLog
{
    private readonly ConcurrentQueue<Notification> _notifications = new();

    public void Add(Notification notification) => _notifications.Enqueue(notification);

    public IReadOnlyCollection<Notification> All => _notifications.ToArray();
}

/// <summary>
/// Handles <see cref="OrderPlaced"/> received from RabbitMQ and asks the Orders service for details over HTTP.
/// </summary>
public class OrderPlacedHandler : HandleEvent<OrderPlaced>
{
    private readonly INotificationLog _notifications;

    public OrderPlacedHandler(ILogger log, ICorrelation correlation, ISendCommand commandSender, ISendQuery querySender, IPublishEvent eventPublisher, INotificationLog notifications)
        : base(log, correlation, commandSender, querySender, eventPublisher)
    {
        _notifications = notifications;
    }

    public override async Task HandleAsync(OrderPlaced @event, CancellationToken cancellationToken = default)
    {
        // Query routed to the "http" sender, i.e. POST {Orders}/api/generic with domain-type header.
        var order = await SendAsync(new GetOrder(@event.OrderId), cancellationToken);
        var message = $"Customer {order.CustomerId}: {order.Quantity} x {order.Product} ordered";
        Log.Information("Notification for order {orderId}: {message}", order.OrderId, message);
        _notifications.Add(new Notification(order.OrderId, message, Correlation.CorrelationId));
    }
}

public class OrderCancelledHandler : HandleEvent<OrderCancelled>
{
    private readonly INotificationLog _notifications;

    public OrderCancelledHandler(ILogger log, ICorrelation correlation, ISendCommand commandSender, ISendQuery querySender, IPublishEvent eventPublisher, INotificationLog notifications)
        : base(log, correlation, commandSender, querySender, eventPublisher)
    {
        _notifications = notifications;
    }

    public override Task HandleAsync(OrderCancelled @event, CancellationToken cancellationToken = default)
    {
        _notifications.Add(new Notification(@event.OrderId, "Order cancelled", Correlation.CorrelationId));
        return Task.CompletedTask;
    }
}
