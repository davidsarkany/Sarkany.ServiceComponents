using FluentValidation;
using ServiceComponents.Api.Mediator;

namespace Orders.Api;

/// <summary>Places a new order. The client chooses the order id, which makes retries idempotent.</summary>
public class PlaceOrder : Command
{
    public string OrderId { get; }
    public string CustomerId { get; }
    public string Product { get; }
    public int Quantity { get; }

    public PlaceOrder(string orderId, string customerId, string product, int quantity, string commandId = default)
        : base(commandId)
    {
        OrderId = orderId;
        CustomerId = customerId;
        Product = product;
        Quantity = quantity;
    }
}

public class PlaceOrderValidator : AbstractValidator<PlaceOrder>
{
    public PlaceOrderValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.Product).NotEmpty();
        RuleFor(c => c.Quantity).InclusiveBetween(1, 100);
    }
}

/// <summary>Cancels an existing order.</summary>
public class CancelOrder : Command
{
    public string OrderId { get; }

    public CancelOrder(string orderId, string commandId = default) : base(commandId)
    {
        OrderId = orderId;
    }
}

public class CancelOrderValidator : AbstractValidator<CancelOrder>
{
    public CancelOrderValidator() => RuleFor(c => c.OrderId).NotEmpty();
}
