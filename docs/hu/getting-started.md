**Magyar** | [English](../en/getting-started.md)

# Első lépések

Ez a leírás a [`samples/`](../../samples) Orders szolgáltatását építi fel. A kész kód itt található:

- `samples/Orders.Api`
- `samples/Orders.Application`
- `samples/Orders.Service`

## 1. Projektek

Egy szolgáltatás általában három projektből áll:

| Projekt | Hivatkozik | Tartalom |
|---|---|---|
| `Orders.Api` | `ServiceComponents.Api` | Commandok, queryk, eventek és validátorok: ezt a kontraktust hivatkozzák a többi szolgáltatások. |
| `Orders.Application` | `ServiceComponents.Application`, `Orders.Api` | Handlerek és a függőségeik. |
| `Orders.Service` | `ServiceComponents.AspNet`, `Orders.Application` | ASP.NET host, wireup, a generic HTTP endpoint. |

## 2. Üzenetek

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

Szabályok az üzenettípusokhoz:

- A property-k csak getterrel rendelkezzenek, és konstruktoron keresztül kapják az értéküket. A konstruktorparaméterek neve egyezzen a property-k nevével, mert a Newtonsoft.Json ezt a konstruktort használja a deserializáláskor.
- **Mindig vegyél fel opcionális `commandId` / `queryId` / `eventId` paramétert, és add tovább az ősosztály konstruktorának.** Enélkül a fogadó oldalon nem áll vissza az azonosító, hanem új GUID keletkezik.
- A queryk a `Query<TResult>` osztályból származnak, az eredménytípusnak Newtonsoft.Jsonnel szerializálhatónak kell lennie.
- A validátorok FluentValidation `AbstractValidator<T>` osztályok az API assembly-ben.

## 3. Handlerek

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

- A `HandleCommand<T>`, `HandleQuery<T,TResult>` és `HandleEvent<T>` ősosztály a `Log`, a `Correlation`, a `SendAsync` és a `PublishAsync` tagokat adja.
- A handler lehet publikus vagy internal osztály is (0.3-tól; a 0.2-ben publikusnak kellett lennie, mert a dispatch `dynamic`-ot használt).
- Egy osztály több handler-interfészt is implementálhat; a 0.3-tól egyszer regisztrálódik (a 0.2-ben az ilyen eventeket háromszor kezelte).
- A várt hibákra `NotFoundException`-t, `BusinessException`-t vagy annak leszármazottját dobd. Ezek 404-es, illetve 400-as válaszra képződnek le (lásd [HTTP transport](transports/http.md)).

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

Amire figyelni kell:

- **Regisztráld az `IExceptionMapperService`-t.** Az `ErrorHandlingMiddleware` és a HTTP senderek is igénylik, és nincs alapértelmezett regisztrációja.
- **Vedd fel a host assembly-t MVC application partként.** A `UseDefault` az entry assembly-t veszi fel, ami `WebApplicationFactory` alatt a tesztfuttató.
- **A metódus neve legyen `CreateHostBuilder`, public static, `string[] args` paraméterrel.** A `ServiceComponents.Testing` ez alapján találja meg a hostot.
- **Ügyelj a callbackek sorrendjére.** A regisztrálás sorrendjében futnak, ezért a middleware-eket az `AddEndpoints` előtt add hozzá a `ConfigureApp`-pal.
- **Az `AddRabbit` induláskor csatlakozik.** A container felépítésekor deklarálja a topológiát, és hibával leáll, ha a broker nem érhető el.

## 5. A generic endpoint

Minden szolgáltatás egyetlen endpointon fogadja az összes üzenetét. Másold át a `samples/Orders.Service/Controllers/GenericController.cs` fájlt:

```csharp
[Route("api/[controller]")]
[ApiController]
public class GenericController : ControllerBase
{
    // konstruktor: HttpRequestParser, IReceiveHttpCommand, IReceiveHttpQuery, IReceiveHttpEvent

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

## 6. Kipróbálás

```bash
dotnet run --project samples/Orders.Service --urls http://localhost:5100

curl -X POST http://localhost:5100/api/generic \
  -H "domain-type: Orders.Api.PlaceOrder, Orders.Api" -H "correlation-id: demo" \
  -d '{"OrderId":"o-1","CustomerId":"c-1","Product":"book","Quantity":2}'

curl -X POST http://localhost:5100/api/generic \
  -H "domain-type: Orders.Api.GetOrder, Orders.Api" -d '{"OrderId":"o-1"}'
```

A példában a RabbitMQ alapból ki van kapcsolva. A két szolgáltatás RabbitMQ-val és Seq-kel együtt így indítható:

```bash
docker compose -f samples/docker-compose.yml up --build
```

## 7. Tesztelés

Lásd a [Tesztelés](testing.md) oldalt és a `samples/Orders.Tests` projektet.
