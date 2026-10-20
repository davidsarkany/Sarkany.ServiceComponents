[Magyar](../../hu/transports/rabbitmq.md) | **English**

# RabbitMQ transport

## Quick setup: `AddRabbit`

```csharp
hostBuilder.AddRabbit(
    connectionString: "amqp://guest:guest@localhost:5672",
    clientName: "orders",
    queue: "orders",
    exchange: "orders-events",
    routingKey: "",
    retryIntervals: new[] { 1000, 5000 });   // optional, milliseconds
```

`AddRabbit` registers the following.

**Connections and channels**

| Purpose | Client name | Connection | Channel |
|---|---|---|---|
| Topology declaration | `{clientName}-generic` | default | default |
| Senders | `{clientName}-publisher` | key `publisher` | key `publisher` |
| Consumers | `{clientName}-consumer` | key `consumer` | one per consumer |

**Consumers**

- Count: `Environment.ProcessorCount - retryIntervals.Length`, at least 1.
- Each consumer `i` gets its own channel and consumer tag `{clientName}-consumer-{i}`.
- With `retryIntervals`, one extra consumer per interval reads `{queue}-retry-{ttl}` (tag `{clientName}-consumer-retry-{ttl}`).

**Senders and receivers**

- Senders are keyed `rabbit`: an event publisher (exchange, routing key), a command sender (exchange, routing key; 0.2 used an empty key) and a query sender.
- The receivers and a startup component are registered too.

**Startup**

When the container is built, the startup component does three things:

1. declares the exchange, the queue and its binding;
2. declares the retry topology, if `retryIntervals` is set;
3. starts all consumers.

**The broker must be reachable at that point.**

### Pitfalls

- **`Func<IConfiguration, …>` overload (0.2 only).** In 0.2 it called the optional routing key and retry builders even when they were null, and threw `NullReferenceException`; pass all six builder functions when you still run 0.2. Fixed in 0.3.
- **Routing key (0.2 only).** In 0.2, with a non-empty routing key the queue is bound with that key, but `AddRabbit`'s command sender publishes with an empty key, so commands never arrive. Fixed in 0.3.
- **Name the queue like the exchange if you send queries over RabbitMQ.** Query RPC publishes to the default exchange, using the *exchange name* as routing key. Only a queue named like the exchange receives the queries; otherwise they are lost and the caller waits until its token is cancelled.

## Topology

Everything is declared **non-durable and auto-delete**. Exchanges are `direct`.

| Object | Name | Arguments / binding |
|---|---|---|
| Exchange | `{exchange}` | direct |
| Queue | `{queue}` | no arguments; with retry: `x-dead-letter-exchange={queue}-dlx-wait-{ttl0}` |
| Binding | `{queue}` ← `{exchange}` | routing key `routingKey` |

With `retryIntervals = [t0, t1, …]`, for each `ttl` in that list:

| Object | Name | Arguments |
|---|---|---|
| Exchange | `{queue}-dlx-wait-{ttl}` | |
| Exchange | `{queue}-dlx-retry-{ttl}` | |
| Queue | `{queue}-wait-{ttl}` | `x-message-ttl={ttl}` (int), `x-dead-letter-exchange={queue}-dlx-retry-{ttl}`, bound to `{queue}-dlx-wait-{ttl}` with "" |
| Queue | `{queue}-retry-{ttl}` | `x-dead-letter-exchange` = next `{queue}-dlx-wait-{ttl+1}`, or `{queue}-dlx` for the last; bound to `{queue}-dlx-retry-{ttl}` with "" |

Finally the exchange `{queue}-dlx` and the queue `{queue}-dlx`, bound with "".

A failing message goes through these steps:

```mermaid
flowchart LR
  Q["{queue}"] -- reject --> W1["{queue}-wait-t0"] -- ttl --> R1["{queue}-retry-t0"]
  R1 -- reject --> W2["{queue}-wait-t1"] -- ttl --> R2["{queue}-retry-t1"]
  R2 -- reject --> D["{queue}-dlx"]
```

- **Total attempts:** 1 + number of intervals. After the last one the message stays in `{queue}-dlx`.
- **Routing keys.** Dead-lettered messages keep their routing key. In 0.2 the retry bindings used only "", so messages published with a non-empty routing key were lost at the first retry. Since 0.3 the retry chain is also bound with the configured routing key; `RoutableEvent`s with other keys are still not retried.
- **Changing the retry setting breaks startup.** The main queue's arguments differ with and without retry, so an existing queue fails redeclaration with `PRECONDITION_FAILED`.

## Messages on the wire

| Property | Command | Query | Event |
|---|---|---|---|
| Exchange | configured exchange | `""` (default exchange) | configured exchange |
| Routing key | configured routing key | the configured *exchange* name | `RoutableEvent.RoutingKey`, or the configured routing key |
| `Type` | `"<FullName>, <AssemblyName>"` | same | same; for a `RoutableEvent`, the type of the inner event |
| `MessageId` | | | `EventId` |
| `CorrelationId` | current `CorrelationId`, or a new GUID | same | same |
| `ReplyTo` | | server-named, exclusive reply queue | |
| Header `causation_id` | current `CurrentId` (string), only when set | same | same |
| Body | UTF-8 JSON (default Newtonsoft settings) | same | the inner event for `RoutableEvent` |

No `ContentType`, `DeliveryMode` (messages are transient) or timestamp is set.

### Consuming

- Consumers run with manual ack and **no prefetch limit** by default (`BasicQos` is only called when `RabbitMessagingOptions.PrefetchCount` is set, 0.3).
- For each delivery the consumer:
  1. creates a new lifetime scope;
  2. parses the body using the `Type` property (or an embedded `$type`);
  3. sets the correlation;
  4. dispatches the message through the pipeline.
- On success it acks.
- For a query it then publishes the JSON result to `ReplyTo`, with the request's `CorrelationId`. A `null` result is answered with `null` since 0.3 (0.2 sent no reply). A failure while replying is logged and does not reject the already acked message (0.3).
- On **any** exception it rejects with `requeue: false`. The message goes to the retry chain if one is configured, and is dropped otherwise. Query callers get no reply, unless `ReplyOnQueryFailure` is enabled (opt-in, 0.3).
- Bodies that are not a command, query or event go to `IReceiveRabbitMessage`, if you register one.

### Query RPC (sender side)

1. Declares a server-named exclusive reply queue and consumes it with auto-ack.
2. Publishes the query and waits for the reply with the same `CorrelationId`.
3. Deletes the reply queue and deserializes the result.

By default there is **no timeout**: pass a `CancellationToken`, or set `RabbitMessagingOptions.QueryTimeoutInSeconds` (0.3).

## Messaging options (0.3)

`RabbitMessagingOptions` is read from a registered instance or the `ServiceComponents:RabbitMessagingOptions` section: `QueryTimeoutInSeconds`, `PrefetchCount` and `ReplyOnQueryFailure`. See the [migration notes](../migration/0.2-to-0.3.md#opt-in-changes) before enabling `ReplyOnQueryFailure`.

## Lower-level registration

The pieces `AddRabbit` uses are public:

| Method | Registers |
|---|---|
| `AddRabbitConnection(uri, clientName, key)` | Synchronous connection; connects on first resolve |
| `AddRabbitChannel(connectionKey, key)` | Channel on that connection |
| `AddRabbitConsumer(queue, consumerTag, channelKey, consumerKey)` | Consumer |
| `AddRabbitReceivers()` | Rabbit receivers |
| `AddRabbitReceiverCorrelationBehavior()` | Correlation from message properties |
| `AddRabbitCommandSender(exchange, routingKey, channelKey, key)` | Command sender |
| `AddRabbitQuerySender(exchange, routingKey, channelKey, key)` | Query sender |
| `AddRabbitEventPublisher(exchange, routingKey, mandatory, basicProperties, channelKey, key)` | Event publisher (`mandatory` and the `basicProperties` template apply since 0.3; 0.2 ignored them) |
| `AddRabbitSenderCorrelationBehavior()` | Correlation on outgoing messages |
| `AddRabbitRetryConsumers(connectionKey, queue, ttls, clientName)` | Retry consumers |
| `IChannel.AddRabbitRetry(scope, queue, ttls)` | Declares the retry topology |
| `IChannel.AddRabbitRetry(scope, queue, ttls, routingKey)` | Same, also bound with the routing key (0.3) |

### Asynchronous initialization

For new code, connections and channels can be opened asynchronously after the container is built, so registration performs no network I/O:

```csharp
builder.RegisterRabbitConnection(new Uri("amqp://localhost"), "client");
builder.RegisterRabbitChannel();
var container = builder.Build();
await container.InitializeRabbitAsync(cancellationToken);   // before resolving IConnection / IChannel
```

- **Initialization:** concurrent calls share one initialization. On failure, partially opened resources are disposed.
- **Disposal:** disposing the container closes channels before connections.
- **Resolving too early:** resolving before initialization throws, and the message names `InitializeRabbitAsync`.

## Connection options

Bound from `ServiceComponents:RabbitConnectionOptions`. The URI and client name passed in code override the configuration.

| Option | Default | Note |
|---|---|---|
| `EndpointUri` | required | |
| `ClientName` | required | |
| `AutomaticRecoveryEnabled` | `true` | |
| `HandshakeContinuationTimeoutInSeconds` | `120` | |
| `RequestedHeartbeatInSeconds` | `60` | |
| `RequestedChannelMaxInSeconds` | `2048` | A channel count, despite the name |
| `NetworkRecoveryIntervalInSeconds` | `5` | |
| `Ssl` | `false` | Only applies when `true` (0.3); 0.2 overrode `amqps` from the URI |
| `VirtualHost` | `/` | Only applies when not `/` (0.3); 0.2 overrode the vhost from the URI |
