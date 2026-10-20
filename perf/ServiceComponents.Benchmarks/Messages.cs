using FluentValidation;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Mediator;

namespace ServiceComponents.Benchmarks;

public class BenchCommand : Command
{
    public string Name { get; }
    public int Amount { get; }

    public BenchCommand(string name, int amount, string commandId = default) : base(commandId)
    {
        Name = name;
        Amount = amount;
    }
}

public class BenchCommandValidator : AbstractValidator<BenchCommand>
{
    public BenchCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0);
    }
}

public class LargeCommand : Command
{
    public List<LineItem> Items { get; }

    public LargeCommand(List<LineItem> items, string commandId = default) : base(commandId)
    {
        Items = items;
    }
}

public class LineItem
{
    public string Sku { get; set; }
    public string Description { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public class BenchQuery : Query<BenchResult>
{
    public int Id { get; }

    public BenchQuery(int id, string queryId = default) : base(queryId)
    {
        Id = id;
    }
}

public class BenchResult
{
    public int Id { get; set; }
    public string Text { get; set; }
}

public class BenchEvent : Event
{
    public string Name { get; }

    public BenchEvent(string name, string eventId = default) : base(eventId)
    {
        Name = name;
    }
}

public class BenchCommandHandler : IHandleCommand<BenchCommand>
{
    public Task HandleAsync(BenchCommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class LargeCommandHandler : IHandleCommand<LargeCommand>
{
    public Task HandleAsync(LargeCommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class BenchQueryHandler : IHandleQuery<BenchQuery, BenchResult>
{
    public Task<BenchResult> HandleAsync(BenchQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BenchResult { Id = query.Id, Text = "result" });
}

public class BenchEventHandlerA : IHandleEvent<BenchEvent>
{
    public Task HandleAsync(BenchEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class BenchEventHandlerB : IHandleEvent<BenchEvent>
{
    public Task HandleAsync(BenchEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public static class Payloads
{
    public static LargeCommand Large(int items) => new(Enumerable.Range(0, items).Select(i => new LineItem {
        Sku = $"SKU-{i:00000}",
        Description = "A reasonably long product description used to make the payload realistic",
        Quantity = i % 7 + 1,
        Price = 9.99m + i
    }).ToList());

    public static string TypeName(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";
}
