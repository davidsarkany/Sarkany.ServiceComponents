[Magyar](../hu/getting-started.md) | **English**

# Getting started

This walk-through builds the Orders service of [`samples/`](../../samples). The finished code is in:

- `samples/Orders.Api`
- `samples/Orders.Application`
- `samples/Orders.Service`

## 1. Projects

A service is usually split into three projects:

| Project | References | Contains |
|---|---|---|
| `Orders.Api` | `ServiceComponents.Api` | Commands, queries, events and validators: the contract other services reference. |
| `Orders.Application` | `ServiceComponents.Application`, `Orders.Api` | Handlers and their dependencies. |
| `Orders.Service` | `ServiceComponents.AspNet`, `Orders.Application` | ASP.NET host, wireup, the generic HTTP endpoint. |

## 2. Messages

```csharp
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
        RuleFor(c => c.Quantity).InclusiveBetween(1, 100);
    }
}
```

Rules for message types:

- Make properties getter-only and set them through a constructor whose parameter names match the property names. Newtonsoft.Json uses that constructor when it deserializes the message.
- **Always add the optional `commandId` / `queryId` / `eventId` parameter and pass it to the base constructor.** Without it the id is not restored on the receiving side, and a new GUID is generated there.
- Queries derive from `Query<TResult>`. The result type must be serializable with Newtonsoft.Json.
- Validators are FluentValidation `AbstractValidator<T>` classes in the API assembly.

## 3. Handlers

```csharp
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
        if (!_store.TryAdd(new OrderDto { OrderId = command.OrderId, /* ... */ })) {
            throw new ConflictException($"Order {command.OrderId} already exists");   // HTTP 400, "business"
        }

        await PublishAsync(new OrderPlaced(command.OrderId, command.CustomerId, command.Product, command.Quantity), cancellationToken);
    }
}
```

- The base classes `HandleCommand<T>`, `HandleQuery<T,TResult>` and `HandleEvent<T>` give you `Log`, `Correlation`, `SendAsync` and `PublishAsync`.
- Handlers may be public or internal classes (Sarkany; the original required public handlers because dispatch used `dynamic`).
- One class may implement several handler interfaces; in Sarkany it is registered once (the original handled such events three times).
- Throw `NotFoundException`, `BusinessException` or its subclasses for expected failures. They map to 404 and 400 responses (see [HTTP transport](transports/http.md)).

## 4. Host

```csharp
public static IHostBuilder CreateHostBuilder(string[] args)
{
    return new ServiceComponentsHostBuilder()
        .UseDefault(new[] { typeof(PlaceOrder).Assembly }, new[] { typeof(PlaceOrderHandler).Assembly })
        .UseSerilog((context, log) => log.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext().WriteTo.Console())
        .ConfigureApp((configuration, environment, app) => {
            app.UseMiddleware<ErrorHandlingMiddleware>();
            app.UseRouting();
        })
        .AddEndpoints()
        .ConfigureMvc(mvc => mvc.AddApplicationPart(typeof(Program).Assembly).AddNewtonsoftJson())
        .AddHealthCheck((configuration, checks) => { })
        .AddPrometheusMetrics()
        .ConfigureContainer((context, container) => {
            container.RegisterType<InMemoryOrderStore>().As<IOrderStore>().SingleInstance();
            container.RegisterType<JsonExceptionMapper>().As<IExceptionMapperService>().SingleInstance();
        })
        .AddCommandRouter(command => "loopback")
        .AddQueryRouter(query => "loopback")
        .AddEventRouter(@event => "rabbit")
        .AddRabbit("amqp://guest:guest@localhost:5672", "orders", "orders", "orders-events")
        .CreateHostBuilder(args);
}
```

Things to get right:

- **Register `IExceptionMapperService`.** `ErrorHandlingMiddleware` and the HTTP senders need it, and there is no default registration.
- **Add the host assembly as an MVC application part.** `UseDefault` adds the entry assembly, which is the test runner under `WebApplicationFactory`.
- **Name the method `CreateHostBuilder`, make it public static, and give it a `string[] args` parameter.** `ServiceComponents.Testing` finds the host through this method.
- **Mind the order of callbacks.** They run in registration order, so register middleware with `ConfigureApp` before calling `AddEndpoints`.
- **`AddRabbit` connects at startup.** It declares the topology while the container is being built and fails if the broker is unreachable.

## 5. The generic endpoint

Each service exposes one endpoint for all of its messages. Copy `samples/Orders.Service/Controllers/GenericController.cs`:

```csharp
[Route("api/[controller]")]
[ApiController]
public class GenericController : ControllerBase
{
    // ctor: HttpRequestParser, IReceiveHttpCommand, IReceiveHttpQuery, IReceiveHttpEvent

    [HttpPost]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        object result = null;
        await _parser.Parse(Request,
            async (command, ct) => await _commandReceiver.ReceiveAsync(command, ct),
            async (query, ct) => result = await _queryReceiver.ReceiveAsync((dynamic)query, ct),
            async (@event, ct) => await _eventReceiver.ReceiveAsync(@event, ct),
            cancellationToken);
        return result == null ? Ok() : Ok(result);
    }
}
```

## 6. Try it

```bash
dotnet run --project samples/Orders.Service --urls http://localhost:5100

curl -X POST http://localhost:5100/api/generic \
  -H "domain-type: Orders.Api.PlaceOrder, Orders.Api" -H "correlation-id: demo" \
  -d '{"OrderId":"o-1","CustomerId":"c-1","Product":"book","Quantity":2}'

curl -X POST http://localhost:5100/api/generic \
  -H "domain-type: Orders.Api.GetOrder, Orders.Api" -d '{"OrderId":"o-1"}'
```

RabbitMQ is off by default in the sample. To run both services with RabbitMQ and Seq:

```bash
docker compose -f samples/docker-compose.yml up --build
```

## 7. Test it

See [Testing](testing.md) and `samples/Orders.Tests`.
