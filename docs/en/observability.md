[Magyar](../hu/observability.md) | **English**

# Observability

## Logging

Components receive a `Serilog.ILogger` from the container. Register Serilog on the host builder:

```csharp
hostBuilder.UseSerilog();   // reads the "Serilog" configuration section
hostBuilder.UseSerilog((context, log) => log.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext().WriteTo.Console());
```

### Properties

| Source | Property | Content |
|---|---|---|
| `AddReceiverCorrelationLogEnricherBehavior` (in `UseDefault`), pushed to the `LogContext` | `correlation-id` | Only when set |
| | `causation-id` | Only when set |
| | `command-id` / `query-id` / `event-id` | Id of the received message |
| `LogBehavior`, structured context | `commandData`, `queryData`, `eventData` | The message, destructured |
| | `queryResult` | The query result |
| Correlation behaviors | `correlation` | The correlation object |
| Message templates | `{commandType}`, `{queryType}`, `{eventType}` | Message type |
| | `{handlerType}` | Handler type |
| | `{exchange}`, `{routingKey}`, `{queue}` | RabbitMQ names |
| | `{duration}` | Elapsed time |

- The `LogContext` property names come from `CorrelationLogOptions`, which is resolved as `IOptions<CorrelationLogOptions>` and never bound by the library. To rename them, register or configure the options yourself.
- `Enrich.FromLogContext()` is required to see the `LogContext` properties.

### Levels

| Level | What is logged |
|---|---|
| Verbose | Handling, success and duration of commands and queries |
| Debug | Handling of events |
| Error | Failures (with the exception) |

With `MinimumLevel.Information` only failures are written. Since 0.3 `ErrorHandlingMiddleware` also logs failures mapped to 5xx (`Error`) and client errors (`Debug`).

## Metrics (Prometheus)

```csharp
hostBuilder.AddPrometheusMetrics();
// or: builder.AddPrometheusRequestMetricsService(); builder.AddPrometheusRequestMetricsBehavior();
```

All metrics are in the prometheus-net default registry, without a prefix. They are recorded at the receivers, so loopback, HTTP and RabbitMQ requests are all counted.

| Metric | Type | Help | Labels |
|---|---|---|---|
| `generic_request_counter` | counter | Request counter | `namespace`, `name`, `kind` |
| `request_failure_counter` | counter | Request failure counter | `exception_name`, `namespace`, `name`, `kind` |
| `request_duration` | summary (ms) | Request duration | `namespace`, `name`, `kind` |
| `generic_request_gauge` | gauge | Actual request counter | `Name`, `Title`, `TypeId` (constant values) |

**Label values**

- `namespace` and `name` are the message type's namespace and name.
- `kind` is `command`, `query` or `event`.
- `exception_name` is the exception type's short name.

**Duration semantics**

- Command durations are recorded on failure too.
- Query and event durations are recorded only on success.

**Endpoint and custom metrics**

- `GET /metrics` (`MetricsController` in `ServiceComponents.AspNet`) returns the Prometheus text format. Add the assembly as an MVC application part if it is not discovered.
- Custom metrics: annotate a class with `[CounterMetric]`, `[GaugeMetric]`, `[SummaryMetric]`, `[LinearHistogramMetric]` or `[ExponentialHistogramMetric]`. Its public properties become the labels, named by `[MetricField("name")]`. Record values with `IMetricsService` (`Increment`, `Decrement`, `Set`, `Observe`, `using (metrics.Observe(metric)) { … }`).

## Health checks

```csharp
hostBuilder.AddHealthCheck((configuration, checks) => {
    checks.AddConsumers("rabbit-consumers", new[] { ServiceComponentsHealthcheckExtensions.ReadinessTag });
    checks.AddRedis("localhost");
}, readinessPath: "/.well-known/ready", livenessPath: "/.well-known/live");
```

**Endpoints**

| Endpoint | Includes checks |
|---|---|
| `/.well-known/ready` | untagged or tagged `Readiness` |
| `/.well-known/live` | untagged or tagged `Liveness` |

- The built-in `self` check is tagged `Liveness`.
- Responses are `Healthy`/`Unhealthy` with status 200/503.

**RabbitMQ consumers**

`AddConsumers` reports each consumer by its consumer tag:

- data: `"Healthy"` or `"Unhealthy"` per tag;
- descriptions: "All consumers are healthy" and "One or more consumer is not healthy".
