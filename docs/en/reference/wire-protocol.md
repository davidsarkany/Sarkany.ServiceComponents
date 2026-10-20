[Magyar](../../hu/reference/wire-protocol.md) | **English**

# Wire protocol reference

This page is **normative**. It lists everything other services, dashboards and configuration depend on. Any version of ServiceComponents must keep it unchanged so that old and new services can work together.

Each item is pinned by tests in `src/ServiceComponents.UnitTests` (U) or `src/ServiceComponents.IntegrationTests` (I).

## Message serialization (U: `Api/MessageTests`, `Infrastructure/RequestParserTests`)

**Type name**

The type name is `"{Type.FullName}, {AssemblyName}"`, without version, culture or public key token.

**Body**

The body is `JsonConvert.SerializeObject(message, Formatting.None)` with default Newtonsoft settings:

- property names as declared (PascalCase);
- nulls included;
- no `$type`.

**Ids**

`CommandId`, `QueryId` and `EventId` are getter-only. They are restored through the constructor parameters `commandId`, `queryId` and `eventId`, and a new GUID is generated when the value is missing.

**Parsing incoming bodies**

| Input | Handling |
|---|---|
| Type name given | Deserialize into `Type.GetType(typeName)` with default settings |
| No type name | Deserialize with `TypeNameHandling.All` and `TypeNameAssemblyFormatHandling.Simple`, so a `$type` is required |
| Empty body | Treated as `{}` |
| Unparsable JSON | Becomes the string `""` |
| Not a command, query or event | Raw-message callback if present, otherwise `InvalidOperationException("Unknown request type: <type> (not a command, query or event)")` |

## HTTP (U: `AspNet/HttpReceiverContractTests`, `AspNet/HttpSenderContractTests`)

| | Receiver | Sender |
|---|---|---|
| Endpoint | application-defined, conventionally `POST /api/generic` | configured URI, `POST` |
| Type | header `domain-type` | header `domain-type` (content header) |
| Correlation | header `correlation-id`; GUID when missing | `correlation-id` when set |
| Causation | header `causation-id` | `causation-id` = current id; omitted when null |
| Body | raw UTF-8, content type ignored | `text/plain; charset=utf-8` |

**Responses**

| Outcome | Status | Body |
|---|---|---|
| Success | 200 | Empty, or the query result |
| `UnauthorizedAccessException` | 401 | `{"Type":"security","ErrorMessage":m}` |
| `NotImplementedException` | 501 | `{"Type":"generic","ErrorMessage":m}` |
| `NotFoundException` | 404 | `{"Type":"generic","ErrorMessage":m}` |
| `BusinessException` | 400 | `{"Type":"business","ErrorCode":c,"ErrorMessage":m}` |
| `ValidationException` | 400 | `{"Type":"validation","Errors":[{"PropertyName","ErrorCode","ErrorMessage","AttemptedValue"}],"ErrorMessage":"Validation failed"}` |
| `InvalidOperationException` | 400 | `{"Type":"generic","ErrorMessage":m}` |
| Any other exception | 500 | `{"Type":"generic","ErrorMessage":"Something really bad happened"}` |

**Mapping responses back to exceptions** (sender side)

| Response | Exception |
|---|---|
| 401 | `UnauthorizedAccessException` |
| 501 | `NotImplementedException` |
| 404 | `NotFoundException` |
| 400 + business | `BusinessException(code, m)` |
| 400 + validation | `ValidationException` |
| other 400 | `InvalidOperationException` |
| anything else | `Exception("Unknown error")` |

Since 0.3, bodies that are empty or not error JSON map by status code alone (401, 404, 501 as above; 400 to `InvalidOperationException` with the body text). The bodies on the wire are unchanged.

## RabbitMQ (U: `Rabbit/*`, `AspNet/RabbitWireupTests`; I: `RabbitEndToEndTests`)

**Topology**

- Topology names and arguments are exactly as listed in [RabbitMQ transport](../transports/rabbitmq.md#topology).
- Everything is non-durable and auto-delete; exchanges are `direct`.
- `x-message-ttl` is an int.

**Properties**

| Property | Value |
|---|---|
| `Type` | Type name |
| `CorrelationId` | Always set |
| `MessageId` | `EventId`, events only |
| `ReplyTo` | Queries only |
| Header `causation_id` | String on send, `byte[]` on receive |
| `ContentType`, `DeliveryMode`, timestamp | Not set |

**Routing**

| Message | Exchange | Routing key |
|---|---|---|
| Command | configured exchange | configured key (`AddRabbit`: `""`) |
| Event | configured exchange | `RoutableEvent.RoutingKey`, or the configured key |
| Query | `""` | configured exchange name |

**RPC**

- The reply goes to `ReplyTo` through the default exchange, with only `CorrelationId` set and the JSON result as body.
- The consumer acks before it replies.
- Null results: 0.2 sends no reply; 0.3 replies with the body `null`, which 0.2 senders also read as `null`.
- Failures: no reply by default. With the opt-in `ReplyOnQueryFailure` (0.3) an error reply carries the headers `x-sc-error-kind`, `x-sc-error-message` and `x-sc-error-code`; 0.2 senders would read it as a `null` result.

**Acknowledgement**

Manual ack. On any failure: `BasicReject(requeue: false)`. No QoS is set.

**Consumer tags and Autofac keys** (`AddRabbit`)

| Item | Name |
|---|---|
| Consumer tags | `{client}-consumer-{i}`, `{client}-consumer-retry-{ttl}` |
| Connection client names | `{client}-generic`, `{client}-publisher`, `{client}-consumer` |
| Keys | `publisher`, `consumer`, `consumer-{i}`, `consumer-retry`, `consumer-retry-{ttl}`, `__consumer__`, `rabbit` |

## Correlation and logging (U: `Infrastructure/BehaviorTests`, `Infrastructure/LoopbackAndRoutingTests`)

- `ICorrelation` has the properties `CorrelationId`, `CausationId` and `CurrentId`.
- The `LogContext` properties are `correlation-id`, `causation-id`, `command-id`, `query-id` and `event-id`.
- The structured properties are `correlation`, `commandData`, `queryData`, `eventData` and `queryResult`.

## Metrics and health (U: `Infrastructure/MonitoringTests`, `AspNet/HostingTests`)

- The metric names, help texts, types, label names, label order and duration semantics are listed in [Observability](../observability.md#metrics-prometheus).
- The metrics endpoint is `GET /metrics`.
- The health endpoints are `/.well-known/ready` and `/.well-known/live`.
- The tags are `Readiness`, `Liveness` and the built-in check `self`.

## Configuration (U: `Rabbit/RabbitConnectionOptionsTests`, `Rabbit/RabbitTopologyTests`)

- The section is `ServiceComponents:RabbitConnectionOptions`.
- The property names and defaults are listed in [Configuration](../configuration.md).

## Mediator semantics (U: `Infrastructure/MediatorPipelineTests`)

- The hook order is listed in [Messaging concepts](../concepts/messaging.md#hooks).
- Event handlers run concurrently in a child scope.
- The error message texts of missing handlers do not change.
