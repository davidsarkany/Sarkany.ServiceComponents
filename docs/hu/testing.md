**Magyar** | [English](../en/testing.md)

# Tesztelés

## Szolgáltatás tesztelése a `ServiceComponents.Testing`-gel

A `TestHost<TEntryPoint>` egy `WebApplicationFactory`. A valódi hostodat futtatja memóriában, Autofackel, és a Serilog kimenetét az xUnit outputra köti. A hozzá tartozó xUnit ősosztály a `Test<THost>`.

```csharp
public class OrdersTestHost : TestHost<Program>
{
    public Mock<IPublishEvent> EventPublisher { get; } = new();

    public override void ConfigureTestServices(IServiceCollection services) { }

    public override void ConfigureTestContainer(ContainerBuilder builder)
    {
        builder.UseTestPublisher(EventPublisher.Object);   // utolsóként regisztrálódik, így lecseréli az event routert
    }
}

public class OrdersApiTests : Test<OrdersTestHost>
{
    public OrdersApiTests(OrdersTestHost host, ITestOutputHelper output) : base(host, output) { }

    [Fact]
    public async Task Placing_an_order_publishes_OrderPlaced()
    {
        var response = await Post(new PlaceOrder("o-1", "c-1", "book", 2));   // POST /api/generic domain-type headerrel

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        TestHost.EventPublisher.Published<OrderPlaced>(e => e.OrderId == "o-1");
    }
}
```

A teljes példa a `samples/Orders.Tests` projektben található.

**Követelmények**

- Az entry pointnak legyen publikus, statikus `CreateHostBuilder(string[] args)` metódusa.
- A host assembly legyen MVC application partként felvéve (lásd [Első lépések](getting-started.md)).
- A `UseOutputHelper` egy Seq sinkbe is ír, alapértelmezés szerint a `http://localhost:5341` címre; ha nem fut Seq, az eventek csendben elvesznek. A Sarkany változatban a `ServiceComponents:Testing:SeqServerUrl` konfigurációs kulccsal más cím adható meg, `""` értékkel pedig a sink kikapcsolható.

**Segédek**

| Segéd | Mire való |
|---|---|
| `UseTestPublisher(publisher)` | regisztrál egy `IPublishEvent` példányt |
| `Mock<IPublishEvent>.Published<TEvent>(count)` | ellenőrzi, hány adott típusú event ment ki |
| `Mock<IPublishEvent>.Published<TEvent>(predicate, count)` | ugyanez, szűrőfeltétellel |
| `UseTestSender<TTestHost>(key)` | `ISendHttpCommand` és `ISendHttpQuery` sendert regisztrál, amelyek egy új `TTestHost` `/api/generic` útvonalára küldenek; a host a containerrel együtt záródik le. Kell hozzá `ILogger`, `IOptions<HttpRequestOptions>` és `IExceptionMapperService`. A Sarkany változatban működik (az eredetiben hibára futott), lásd `samples/Orders.Tests/TestSenderTests.cs`. |
| `UseTestSenderAt<TTestHost>(requestUri, key)` | ugyanez, a `requestUri` útvonalára küldve (Sarkany) |

A `UseEventRecorder()` és az `IEventRecorder.WaitFor<TEvent>(filter, timeout)` párossal a teszt megvárhat egy eventet, amelyet a szolgáltatás kezelt.

## A repó tesztkészletei

| Projekt | Tartalom | Kell hozzá |
|---|---|---|
| `src/ServiceComponents.UnitTests` | Minden csomag unit és contract tesztjei: mediator szemantika, wire formátumok (rögzítő `IChannel`-lel), HTTP kontraktus valódi `UseDefault` hoston, metrika-scrape, az ismert hibák karakterizációja | — |
| `src/ServiceComponents.IntegrationTests` | RabbitMQ végponttól végpontig: topológia, commandok, eventek, RPC, retry és dead-lettering | Docker |
| `samples/Orders.Tests` | A `ServiceComponents.Testing` használata | — |
| `perf/` | Benchmarkok és load tesztek (lásd [Teljesítmény](performance.md)) | Docker a RabbitMQ szcenáriókhoz |

Csomagonként minden publikus API tag rögzítve van a `PublicAPI.Shipped.txt`-ben. Ha egy publikus tag eltűnik vagy megváltozik, a [Public API analyzer](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/PublicApiAnalyzers/PublicApiAnalyzers.Help.md) megállítja a buildet.

Minden teszt futtatása lefedettséggel:

```bash
bash build/coverage.sh   # 90% line / 80% branch alatt elbukik; a riport az artifacts/coverage/report mappában van
```

### Karakterizációs tesztek

A `*_current_behavior*` nevű vagy `// BUG:` megjegyzéssel jelölt tesztek a mai viselkedést rögzítik, az ismert hibákkal együtt. Egy javításnak ezeket a teszteket tudatosan kell megváltoztatnia, és a változást fel kell venni a migrációs jegyzetbe. A listájuk az [Ismert hibák](known-issues.md) oldalon található.
