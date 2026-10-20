**Magyar** | [English](../en/observability.md)

# Megfigyelhetőség

## Logolás

A komponensek `Serilog.ILogger`-t kapnak a containerből. A Serilogot a host builderen kell regisztrálni:

```csharp
hostBuilder.UseSerilog();   // a "Serilog" konfigurációs sectiont olvassa
hostBuilder.UseSerilog((context, log) => log.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext().WriteTo.Console());
```

### Property-k

| Forrás | Property | Tartalom |
|---|---|---|
| `AddReceiverCorrelationLogEnricherBehavior` (a `UseDefault` része), a `LogContext`-be | `correlation-id` | csak ha be van állítva |
| | `causation-id` | csak ha be van állítva |
| | `command-id` / `query-id` / `event-id` | a fogadott üzenet azonosítója |
| `LogBehavior`, strukturált kontextusként | `commandData`, `queryData`, `eventData` | az üzenet, kibontva |
| | `queryResult` | a query eredménye |
| Correlation behavior-ök | `correlation` | a correlation objektum |
| Üzenetsablonok | `{commandType}`, `{queryType}`, `{eventType}` | üzenettípus |
| | `{handlerType}` | handlertípus |
| | `{exchange}`, `{routingKey}`, `{queue}` | RabbitMQ nevek |
| | `{duration}` | eltelt idő |

- A `LogContext` property-k nevét a `CorrelationLogOptions` adja. Ezt a rendszer `IOptions<CorrelationLogOptions>`-ként oldja fel, a könyvtár viszont sosem köti be konfigurációból. Átnevezéshez regisztráld vagy konfiguráld saját magad.
- A `LogContext` property-k csak `Enrich.FromLogContext()` mellett látszanak.

### Szintek

| Szint | Mit logol |
|---|---|
| Verbose | a commandok és queryk feldolgozása, sikere, időtartama |
| Debug | az eventek feldolgozása |
| Error | a hibák (a kivétellel együtt) |

`MinimumLevel.Information` mellett csak a hibák jelennek meg. A Sarkany változatban az `ErrorHandlingMiddleware` is logolja az 5xx-re leképzett hibákat (`Error`) és a kliens hibákat (`Debug`).

## Metrikák (Prometheus)

```csharp
hostBuilder.AddPrometheusMetrics();
// vagy: builder.AddPrometheusRequestMetricsService(); builder.AddPrometheusRequestMetricsBehavior();
```

Minden metrika a prometheus-net alapértelmezett registry-jébe kerül, prefix nélkül. A mérés a receivereken történik, így a loopback, a HTTP és a RabbitMQ kérések is beleszámítanak.

| Metrika | Típus | Help | Labelek |
|---|---|---|---|
| `generic_request_counter` | counter | Request counter | `namespace`, `name`, `kind` |
| `request_failure_counter` | counter | Request failure counter | `exception_name`, `namespace`, `name`, `kind` |
| `request_duration` | summary (ms) | Request duration | `namespace`, `name`, `kind` |
| `generic_request_gauge` | gauge | Actual request counter | `Name`, `Title`, `TypeId` (állandó értékek) |

**Labelek**

- `namespace`, `name`: az üzenettípus névtere és neve.
- `kind`: `command`, `query` vagy `event`.
- `exception_name`: a kivételtípus rövid neve.

**Időtartam**

- A command időtartama hiba esetén is rögzül.
- A query és az event időtartama csak siker esetén.

**Endpoint**

A `GET /metrics` (a `ServiceComponents.AspNet` `MetricsController`-e) Prometheus szövegformátumban adja vissza a metrikákat. Ha a controller nem kerül elő, vedd fel az assembly-t MVC application partként.

**Saját metrikák**

1. Jelölj meg egy osztályt a `[CounterMetric]`, `[GaugeMetric]`, `[SummaryMetric]`, `[LinearHistogramMetric]` vagy `[ExponentialHistogramMetric]` attribútummal.
2. Az osztály publikus property-jei lesznek a labelek. A label nevét a `[MetricField("name")]` adja meg.
3. Az értékeket az `IMetricsService`-szel rögzítsd: `Increment`, `Decrement`, `Set`, `Observe`, vagy `using (metrics.Observe(metric)) { … }`.

## Health checkek

```csharp
hostBuilder.AddHealthCheck((configuration, checks) => {
    checks.AddConsumers("rabbit-consumers", new[] { ServiceComponentsHealthcheckExtensions.ReadinessTag });
    checks.AddRedis("localhost");
}, readinessPath: "/.well-known/ready", livenessPath: "/.well-known/live");
```

| Endpoint | Mely checkek számítanak bele |
|---|---|
| `/.well-known/ready` | a tag nélküliek és a `Readiness` taggel jelöltek |
| `/.well-known/live` | a tag nélküliek és a `Liveness` taggel jelöltek |

- A beépített `self` check `Liveness` taget kap.
- A válasz `Healthy` 200-as vagy `Unhealthy` 503-as státusszal.

**RabbitMQ consumerek**

Az `AddConsumers` consumer tagenként jelenti az állapotot (`"Healthy"` / `"Unhealthy"`). A leírás:

- "All consumers are healthy", ha minden consumer rendben van;
- "One or more consumer is not healthy", ha legalább egy nem.
