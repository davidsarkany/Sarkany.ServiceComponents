**English** | [Magyar](README.hu.md)

# ServiceComponents

.NET libraries for message-driven microservices: commands, queries and events with handlers, an extensible pipeline
(validation, logging, correlation, Prometheus metrics), and HTTP, RabbitMQ and in-process transports, wired up with
Autofac and Serilog.

```csharp
new ServiceComponentsHostBuilder()
    .UseDefault(new[] { typeof(PlaceOrder).Assembly }, new[] { typeof(PlaceOrderHandler).Assembly })
    .AddRabbit("amqp://guest:guest@localhost:5672", "orders", "orders", "orders-events")
    .AddEventRouter(@event => "rabbit")
    .CreateHostBuilder(args);
```

> A rewrite of [gsinka/servicecomponents](https://github.com/gsinka/servicecomponents), published as `Sarkany.ServiceComponents.*`.
> It is a drop-in replacement of the original `ServiceComponents.*` 0.2 packages: same assemblies, namespaces, public API and wire protocol.

**Switching from the original packages:** replace each package id, keep your code unchanged.

```xml
<!-- before -->
<PackageReference Include="ServiceComponents.AspNet" Version="0.2.0-beta.12" />
<!-- after -->
<PackageReference Include="Sarkany.ServiceComponents.AspNet" Version="0.3.0-beta.1" />
```

Replace every `ServiceComponents.*` package of a project (and of the libraries it references): both contain the same assemblies, so they cannot be mixed. Details: [migration notes](docs/en/migration/0.2-to-0.3.md#switching-packages).

- **Documentation:** [docs/en](docs/en/README.md) · [docs/hu](docs/hu/README.md)
- **Changes in 0.3:** [CHANGELOG](CHANGELOG.md) · [migration notes](docs/en/migration/0.2-to-0.3.md)
- **Sample services:** [samples/](samples) (`docker compose -f samples/docker-compose.yml up --build`)
- **Tests:** `dotnet test src/ServiceComponents.sln`, with coverage: `bash build/coverage.sh` (integration tests need Docker)
- **Performance:** `bash perf/run.sh` compares this repository with the original 0.2.0-beta.12 packages ([details](docs/en/performance.md))
- **Interoperability:** `bash build/interop.sh` runs the samples built from the original packages and from this repository against each other
- **Drop-in check:** `bash build/dropin.sh` packs this repository and builds and tests the samples against the packages
- **CI / release:** pushes and pull requests run tests with coverage, interop and the drop-in check (`.github/workflows/ci.yml`). Pushing a version tag (`0.3.0-beta.1` or `v0.3.0`) runs the same checks, then publishes the packages to nuget.org and creates a GitHub release (`.github/workflows/cd.yml`).
