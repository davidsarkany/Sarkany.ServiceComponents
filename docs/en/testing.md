[Magyar](../hu/testing.md) | **English**

# Testing

## Testing a service with `ServiceComponents.Testing`

`TestHost<TEntryPoint>` is a `WebApplicationFactory` that runs your real host in memory, with Autofac and Serilog wired to the xUnit output. `Test<THost>` is the matching xUnit base class.

```csharp
public class OrdersTestHost : TestHost<Program>
{
    public Mock<IPublishEvent> EventPublisher { get; } = new();

    public override void ConfigureTestServices(IServiceCollection services) { }

    public override void ConfigureTestContainer(ContainerBuilder builder)
    {
        builder.UseTestPublisher(EventPublisher.Object);   // registered last: replaces the event router
    }
}

public class OrdersApiTests : Test<OrdersTestHost>
{
    public OrdersApiTests(OrdersTestHost host, ITestOutputHelper output) : base(host, output) { }

    [Fact]
    public async Task Placing_an_order_publishes_OrderPlaced()
    {
        var response = await Post(new PlaceOrder("o-1", "c-1", "book", 2));   // POST /api/generic with domain-type

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        TestHost.EventPublisher.Published<OrderPlaced>(e => e.OrderId == "o-1");
    }
}
```

The complete example is in `samples/Orders.Tests`.

**Requirements**

- The entry point must have a public static `CreateHostBuilder(string[] args)`.
- Add the host assembly as an MVC application part (see [Getting started](getting-started.md)).
- `UseOutputHelper` also writes to a Seq sink, by default at `http://localhost:5341`; when no Seq is running, the events are dropped silently. In Sarkany, the configuration key `ServiceComponents:Testing:SeqServerUrl` sets another URL, or disables the sink when set to `""`.

**Helpers**

| Helper | Purpose |
|---|---|
| `UseTestPublisher(publisher)` | Registers an `IPublishEvent` |
| `Mock<IPublishEvent>.Published<TEvent>(count)` | Verifies how many events of a type were published |
| `Mock<IPublishEvent>.Published<TEvent>(predicate, count)` | Same, filtered by a predicate |
| `UseTestSender<TTestHost>(key)` | Registers `ISendHttpCommand` and `ISendHttpQuery` posting to `/api/generic` of a new `TTestHost`; the host is disposed with the container. It needs `ILogger`, `IOptions<HttpRequestOptions>` and `IExceptionMapperService`. Works in Sarkany (it failed in the original); see `samples/Orders.Tests/TestSenderTests.cs`. |
| `UseTestSenderAt<TTestHost>(requestUri, key)` | Same, posting to the path of `requestUri` (Sarkany) |

`UseEventRecorder()` with `IEventRecorder.WaitFor<TEvent>(filter, timeout)` lets a test wait for an event handled by the service.

## The repository's test suites

| Project | Scope | Needs |
|---|---|---|
| `src/ServiceComponents.UnitTests` | Unit and contract tests of every package: mediator semantics, wire formats (with a recording `IChannel`), HTTP contract on a real `UseDefault` host, metrics scraping, characterization of known defects | — |
| `src/ServiceComponents.IntegrationTests` | RabbitMQ end to end (topology, commands, events, RPC, retry and dead lettering) | Docker |
| `samples/Orders.Tests` | Usage of `ServiceComponents.Testing` | — |
| `perf/` | Benchmarks and load tests (see [Performance](performance.md)) | Docker for RabbitMQ scenarios |

Every public API member is recorded in `PublicAPI.Shipped.txt` per package. The [Public API analyzer](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/PublicApiAnalyzers/PublicApiAnalyzers.Help.md) fails the build when a public member is removed or changed.

Run everything with coverage:

```bash
bash build/coverage.sh   # fails below 90% line / 80% branch coverage; report in artifacts/coverage/report
```

### Characterization tests

Tests named `*_current_behavior*` or marked `// BUG:` pin today's behavior, including known defects. A fix must change these tests deliberately and list the change in the migration notes. The [Known issues](known-issues.md) page lists them.
