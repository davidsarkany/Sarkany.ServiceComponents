[Magyar](../../hu/concepts/messaging.md) | **English**

# Messaging concepts

## Messages

| Kind | Base class | Id | Handled by | Result |
|---|---|---|---|---|
| Command | `Command` (`ICommand`) | `CommandId` | exactly one `IHandleCommand<T>` | none |
| Query | `Query<TResult>` (`IQuery<TResult>`) | `QueryId` | exactly one `IHandleQuery<T, TResult>` | `TResult` |
| Event | `Event` (`IEvent`) | `EventId` | zero or more `IHandleEvent<T>` | none |

- **Ids.** When none is passed, a new GUID string is generated.
- **`RoutableEvent(event, routingKey)`.** Wraps an event to publish it to RabbitMQ with a specific routing key. Only the RabbitMQ publisher unwraps it.

## Dispatch

`AddMediator(assemblies)` scans the given assemblies for closed implementations of `IHandleCommand<>`, `IHandleQuery<,>` and `IHandleEvent<>`, and registers them per dependency. The handler is selected by the **runtime type** of the message.

| Situation | Behavior |
|---|---|
| No command handler | `InvalidOperationException("No handler registered for <Type>")` |
| No query handler | `InvalidOperationException("No handler found for <Type>")` |
| No event handler | No-op |
| Several event handlers | Run **concurrently** (`Task.WhenAll`) in a child lifetime scope |

## Hooks

`AddMediatorBehavior(assemblies)` wraps the dispatchers with pre, post and failure hooks found in the assemblies.

Each hook comes in two variants:

- **Non-generic:** sees every message of a kind.
- **Generic:** sees one message type only.

| Hook | Non-generic | Generic |
|---|---|---|
| Before the handler | `IPreHandleCommand` / `Query` / `Event` | `IPreHandleCommand<T>`, `IPreHandleQuery<T,R>`, `IPreHandleEvent<T>` |
| After the handler | `IPostHandleCommand` / `Query` / `Event` | `IPostHandleCommand<T>`, `IPostHandleQuery<T,R>`, `IPostHandleEvent<T>` |
| On exception | `IHandleCommandFailure` / `Query` / `Event` | `IHandleCommandFailure<T>`, `IHandleQueryFailure<T,R>`, `IHandleEventFailure<T>` |

**Execution order** (the post order differs between kinds):

| Kind | Order |
|---|---|
| Command | pre → pre&lt;T&gt; → handler → post&lt;T&gt; → post |
| Query, event | pre → pre&lt;T&gt; → handler → post → post&lt;T&gt; |
| On exception | failure → failure&lt;T&gt;, then the exception is rethrown |

The value returned by `IPostHandleQuery<T,R>.PostHandleAsync` is ignored. The original result is returned to the caller.

## Built-in behaviors

| Registration | Effect |
|---|---|
| `AddValidationBehavior(apiAssemblies)` | Registers FluentValidation validators and runs them as generic pre-hooks; throws `ValidationException`. Requires `AddMediatorBehavior`. |
| `AddLogBehavior()` | Logs handling, success (verbose, events at debug) and failures (error) |
| `AddStopwatchBehavior()` | Logs the duration per request (verbose) |
| `AddCorrelationInfo()` | Registers the scoped `Correlation` / `ICorrelation` |
| `AddReceiverCorrelationLogEnricherBehavior()` | Pushes the correlation ids to the Serilog `LogContext` |
| `AddPrometheusRequestMetricsService()` + `AddPrometheusRequestMetricsBehavior()` | Request metrics (see [Observability](../observability.md)) |
| `AddRequestConstraints(keys, constraint, expiry)` | Limits concurrent execution per key using `IDistributedCache`; throws `CommandConstraintException` (see [Known issues](../known-issues.md)) |
| `UseEventRecorder()` | Records handled events; `IEventRecorder.WaitFor` waits for one (useful in tests) |

`UseDefault` registers mediator, hooks, validation, logging, stopwatch, all correlation behaviors, receivers and the loopback sender.

## Sending and routing

Handlers depend on `ISendCommand`, `ISendQuery` and `IPublishEvent`. With routers registered, these resolve to `CommandRouter` / `QueryRouter` / `EventRouter`, which call a key selector and resolve the sender registered under that key:

```csharp
.AddCommandRouter(command => "loopback")
.AddQueryRouter(query => query is GetOrder ? "http" : "loopback")
.AddEventRouter(@event => "rabbit")
```

| Key | Sender | Registered by |
|---|---|---|
| `loopback` | In-process: dispatches in a new lifetime scope of the same service | `UseDefault` / `AddLoopbackSender("loopback")` |
| `rabbit` | RabbitMQ command sender, query sender (RPC), event publisher | `AddRabbit` |
| your key | HTTP command sender, query sender, event publisher | `AddHttpSender(uri, key)` |

`EventRouter.PublishAsync(IEnumerable<IEvent>)` publishes the events one by one.

## Correlation

Every request scope has one `Correlation`:

| Field | Meaning |
|---|---|
| `CorrelationId` | Shared by every message caused by the same original request |
| `CausationId` | The id of the message that caused this one |
| `CurrentId` | The id of the message being handled |

How each transport fills the correlation of the receiving scope:

| Transport | `CorrelationId` | `CausationId` | `CurrentId` |
|---|---|---|---|
| HTTP | `correlation-id` header, or a new GUID | `causation-id` header | message id |
| RabbitMQ | `CorrelationId` property, or a new GUID | `causation_id` header | message id |
| Loopback | parent's `CorrelationId`, or a new GUID | parent's `CurrentId` | message id |

Senders write `CorrelationId` and `CurrentId` (as causation) of the current scope to the wire.

> Event handlers are resolved in a child scope that inherits this correlation (Sarkany; in the original they saw an empty correlation).
