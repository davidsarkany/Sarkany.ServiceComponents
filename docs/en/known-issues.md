[Magyar](../hu/known-issues.md) | **English**

# Known issues

Defects of the current version, each pinned by a characterization test (`// BUG:` or `*_current_behavior*`). The upcoming rewrite fixes them as follows:

- **Compatible:** the fix does not affect services running the old version, so it is applied directly.
- **Opt-in:** the fix changes the wire, so it is behind a switch.

The tests change with the fix, and the change is listed in the migration notes.

## Severe

| # | Issue | Where | Planned fix |
|---|---|---|---|
| 1 | ✅ **Fixed in 0.3.** **Event handlers lose the correlation.** `EventDispatcher` resolves handlers in a child scope with a new, empty `Correlation`, so events sent from event handlers start new correlation chains. | `EventDispatcher` | compatible |
| 2 | ✅ **Fixed in 0.3.** **The vhost and TLS from the RabbitMQ URI are overwritten.** `VirtualHost` (`/`) and `Ssl` (`false`) are applied after the URI, so `amqps://host/vhost` connects to `/` without TLS. | `RabbitAutofacExtensions.CreateConnectionFactory` | compatible: only apply when configured |
| 3 | ✅ **Fixed in 0.3** (null result, cleanup, timeout and failure reply options). **Queries over RabbitMQ can hang forever.** The query is routed to a queue named after the exchange; there is no timeout; no reply is sent for null results or handler failures; the reply queue stays. | `RabbitQuerySender`, `RabbitConsumer` | timeout option (compatible); error reply opt-in |
| 4 | ✅ **Fixed in 0.3.** **The authorization decorator bypasses HTTP correlation.** It forwards to `IReceiveCommand`/`IReceiveQuery` instead of the decorated HTTP receiver. | `HttpCommandAuthorizationBehavior`, `HttpQueryAuthorizationBehavior` | compatible |
| 5 | ✅ **Fixed in 0.3.** **Request constraint counters are wrong.** The decrement is inverted (removes the key when non-zero, stores 0); a second completion throws `ArgumentNullException`; earlier keys are not rolled back when a later key is rejected. | `RequestExecutionConstraintBehavior` | compatible |

## Medium

| # | Issue | Where |
|---|---|---|
| 6 | ✅ **Fixed in 0.3.** `AddRabbit(Func…)` throws `NullReferenceException` when the optional routing key / retry builders are omitted. | `ServiceComponentsRabbitHostBuilderExtensions` |
| 7 | ✅ **Fixed in 0.3.** When the query reply fails after the ack, the message is also rejected; a real broker closes the channel (406). | `RabbitConsumer` |
| 8 | ✅ **Fixed in 0.3.** Routing keys are lost: with a non-empty `routingKey`, `AddRabbit` commands never arrive (sent with ""), and routed messages are lost at the first retry (retry bindings use ""). | `AddRabbit`, `AddRabbitRetry` |
| 9 | ✅ **Fixed in 0.3.** `ValueObject.Equals` is asymmetric for different value counts; `GetHashCode` throws for an empty value list. | `ValueObject` |
| 10 | ✅ **Fixed in 0.3.** `TimeInterval.Contains(DateTime)` compares the wrong way; `DurationRateOf` uses integer division; `Simplify` depends on the input order for touching intervals. | `TimeIntervalExtensions` |
| 11 | ✅ **Fixed in 0.3.** Prometheus: exponential histograms get linear buckets; `Observe(val, count)` uses labels on histograms created without label names; types without an attribute or without a namespace cause `NullReferenceException`. | `PrometheusMetricsService`, `MetricExtensionMethods` |
| 12 | ✅ **Fixed in 0.3.** `EventRecorderService.WaitFor` returns `null` on timeout; a cancelled token throws; a waiter for a base type only matches already recorded events. | `EventRecorderService` |
| 13 | ✅ **Fixed in 0.3.** `JsonExceptionMapper` throws `JsonReaderException` for empty or non-JSON error bodies (e.g. a JWT 401) and `ArgumentNullException` when `type` is missing. | `JsonExceptionMapper` |
| 14 | ✅ **Fixed in 0.3.** `ErrorHandlingMiddleware` does not log. | `ErrorHandlingMiddleware` |
| 15 | ✅ **Fixed in 0.3.** `EventSourcedAggregateRoot` increments the version and stores the event before checking for an `Apply` method; private `Apply` methods of base classes are not found. | `EventSourcedAggregateRoot` |
| 16 | A message without the `commandId`/`queryId`/`eventId` constructor parameter gets a new id on the receiving side. | message contracts |
| 17 | The result of `IPostHandleQuery<T,R>.PostHandleAsync` is ignored. | `QueryDispatchBehavior` |
| 18 | Event handlers run concurrently in one scope, sharing non-thread-safe services such as `ISession`. | `EventDispatcher` |
| 19 | ✅ **Fixed in 0.3.** `StartupCallbacks` are collected but never invoked. | `ServiceComponentsHostBuilder` |
| 20 | ✅ **Fixed in 0.3.** A class implementing several handler interfaces is registered once per scan, so it handles each event three times. | `AddMediator` |
| 21 | ✅ **Fixed in 0.3.** Handlers must be public: dispatch uses `dynamic`, which throws `RuntimeBinderException` for non-public types. | dispatchers, hooks |

## Minor

- ✅ **Fixed in 0.3. Rabbit event publisher:** `AddRabbitEventPublisher` ignored `mandatory` and `basicProperties`.
- ✅ **Fixed in 0.3. HTTP sender:** `HttpSender` appends `accept: application/json` to the client's default headers on every call.
- ✅ **Fixed in 0.3. Request binder:** `RequestBinder` overwrites `Failed()` with `Success` for non-request types.
- **Background queue:** `BackgroundTaskQueue` drops the oldest item when full.
- ✅ **Fixed in 0.3 (root session). NHibernate session:**
  - `NhibernateModule` auto-activates a root `ISession` that lives as long as the application.
  - Building the container requires a reachable database.
- ✅ **Fixed in 0.3 (async dispose; registration stays manual). NHibernate event publisher:** `NHibernateEventPublisherBehavior` publishes in `Dispose` with a blocking `Wait()` and is not registered anywhere.
- ✅ **Fixed in 0.3. Testing:**
  - `UseTestSender` fails because the new test host has no output helper.
  - `UseOutputHelper` always writes to Seq at `localhost:5341`.
- ✅ **Fixed in 0.3. RoutableEvent:** it is unwrapped by the RabbitMQ publisher only; on loopback and HTTP the inner event never reaches its handler.
- ✅ **Fixed in 0.3. CouchDB:** `CouchDbConfigurationSource` does not check the HTTP status code.
- ✅ **Fixed in 0.3. Consumer health check:** `RabbitMQConsumerChecker` throws on duplicate consumer tags.
- ✅ **Fixed in 0.3. Security:** messages without a type name are deserialized with `TypeNameHandling.All`, so a caller can instantiate arbitrary types. The rewrite adds an allowlist of message types.
- ✅ **Fixed in 0.3. Enumeration:** `Enumeration.GetAll` failed when the class has other static fields.
- **Post-handler order:** post-hook order differs between commands and queries/events.
- ✅ **Fixed in 0.3. NotFoundException:** `NotFoundException(string, Exception)` had error code 0, not 404.
