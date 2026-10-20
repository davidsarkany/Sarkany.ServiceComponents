[Magyar](../hu/known-issues.md) | **English**

# Known issues

Defects of the original `ServiceComponents.*` packages, each pinned by a characterization test (`// BUG:` or `*_current_behavior*`). ✅ marks the ones fixed in Sarkany.ServiceComponents:

- **Compatible:** the fix does not affect services running the original packages, so it is applied directly.
- **Opt-in:** the fix changes the wire, so it is behind a switch.

A fixed issue's tests pin the new behavior, and the change is listed in the [migration notes](migration/from-original.md). Entries without ✅ are deliberately unchanged.

## Severe

| # | Issue | Where | Planned fix |
|---|---|---|---|
| 1 | ✅ **Fixed.** **Event handlers lose the correlation.** `EventDispatcher` resolves handlers in a child scope with a new, empty `Correlation`, so events sent from event handlers start new correlation chains. | `EventDispatcher` | compatible |
| 2 | ✅ **Fixed.** **The vhost and TLS from the RabbitMQ URI are overwritten.** `VirtualHost` (`/`) and `Ssl` (`false`) are applied after the URI, so `amqps://host/vhost` connects to `/` without TLS. | `RabbitAutofacExtensions.CreateConnectionFactory` | compatible: only apply when configured |
| 3 | ✅ **Fixed** (null result, cleanup, timeout and failure reply options). **Queries over RabbitMQ can hang forever.** The query is routed to a queue named after the exchange; there is no timeout; no reply is sent for null results or handler failures; the reply queue stays. | `RabbitQuerySender`, `RabbitConsumer` | timeout option (compatible); error reply opt-in |
| 4 | ✅ **Fixed.** **The authorization decorator bypasses HTTP correlation.** It forwards to `IReceiveCommand`/`IReceiveQuery` instead of the decorated HTTP receiver. | `HttpCommandAuthorizationBehavior`, `HttpQueryAuthorizationBehavior` | compatible |
| 5 | ✅ **Fixed.** **Request constraint counters are wrong.** The decrement is inverted (removes the key when non-zero, stores 0); a second completion throws `ArgumentNullException`; earlier keys are not rolled back when a later key is rejected. | `RequestExecutionConstraintBehavior` | compatible |

## Medium

| # | Issue | Where |
|---|---|---|
| 6 | ✅ **Fixed.** `AddRabbit(Func…)` throws `NullReferenceException` when the optional routing key / retry builders are omitted. | `ServiceComponentsRabbitHostBuilderExtensions` |
| 7 | ✅ **Fixed.** When the query reply fails after the ack, the message is also rejected; a real broker closes the channel (406). | `RabbitConsumer` |
| 8 | ✅ **Fixed.** Routing keys are lost: with a non-empty `routingKey`, `AddRabbit` commands never arrive (sent with ""), and routed messages are lost at the first retry (retry bindings use ""). | `AddRabbit`, `AddRabbitRetry` |
| 9 | ✅ **Fixed.** `ValueObject.Equals` is asymmetric for different value counts; `GetHashCode` throws for an empty value list. | `ValueObject` |
| 10 | ✅ **Fixed.** `TimeInterval.Contains(DateTime)` compares the wrong way; `DurationRateOf` uses integer division; `Simplify` depends on the input order for touching intervals. | `TimeIntervalExtensions` |
| 11 | ✅ **Fixed.** Prometheus: exponential histograms get linear buckets; `Observe(val, count)` uses labels on histograms created without label names; types without an attribute or without a namespace cause `NullReferenceException`. | `PrometheusMetricsService`, `MetricExtensionMethods` |
| 12 | ✅ **Fixed.** `EventRecorderService.WaitFor` returns `null` on timeout; a cancelled token throws; a waiter for a base type only matches already recorded events. | `EventRecorderService` |
| 13 | ✅ **Fixed.** `JsonExceptionMapper` throws `JsonReaderException` for empty or non-JSON error bodies (e.g. a JWT 401) and `ArgumentNullException` when `type` is missing. | `JsonExceptionMapper` |
| 14 | ✅ **Fixed.** `ErrorHandlingMiddleware` does not log. | `ErrorHandlingMiddleware` |
| 15 | ✅ **Fixed.** `EventSourcedAggregateRoot` increments the version and stores the event before checking for an `Apply` method; private `Apply` methods of base classes are not found. | `EventSourcedAggregateRoot` |
| 16 | A message without the `commandId`/`queryId`/`eventId` constructor parameter gets a new id on the receiving side. | message contracts |
| 17 | The result of `IPostHandleQuery<T,R>.PostHandleAsync` is ignored. | `QueryDispatchBehavior` |
| 18 | Event handlers run concurrently in one scope, sharing non-thread-safe services such as `ISession`. | `EventDispatcher` |
| 19 | ✅ **Fixed.** `StartupCallbacks` are collected but never invoked. | `ServiceComponentsHostBuilder` |
| 20 | ✅ **Fixed.** A class implementing several handler interfaces is registered once per scan, so it handles each event three times. | `AddMediator` |
| 21 | ✅ **Fixed.** Handlers must be public: dispatch uses `dynamic`, which throws `RuntimeBinderException` for non-public types. | dispatchers, hooks |

## Minor

- ✅ **Fixed. Rabbit event publisher:** `AddRabbitEventPublisher` ignored `mandatory` and `basicProperties`.
- ✅ **Fixed. HTTP sender:** `HttpSender` appends `accept: application/json` to the client's default headers on every call.
- ✅ **Fixed. Request binder:** `RequestBinder` overwrites `Failed()` with `Success` for non-request types.
- **Background queue:** `BackgroundTaskQueue` drops the oldest item when full.
- ✅ **Fixed (root session). NHibernate session:**
  - `NhibernateModule` auto-activates a root `ISession` that lives as long as the application.
  - Building the container requires a reachable database.
- ✅ **Fixed (async dispose; registration stays manual). NHibernate event publisher:** `NHibernateEventPublisherBehavior` publishes in `Dispose` with a blocking `Wait()` and is not registered anywhere.
- ✅ **Fixed. Testing:**
  - `UseTestSender` fails because the new test host has no output helper.
  - `UseOutputHelper` always writes to Seq at `localhost:5341`.
- ✅ **Fixed. RoutableEvent:** it is unwrapped by the RabbitMQ publisher only; on loopback and HTTP the inner event never reaches its handler.
- ✅ **Fixed. CouchDB:** `CouchDbConfigurationSource` does not check the HTTP status code.
- ✅ **Fixed. Consumer health check:** `RabbitMQConsumerChecker` throws on duplicate consumer tags.
- ✅ **Fixed. Security:** messages without a type name are deserialized with `TypeNameHandling.All`, so a caller can instantiate arbitrary types. The rewrite adds an allowlist of message types.
- ✅ **Fixed. Enumeration:** `Enumeration.GetAll` failed when the class has other static fields.
- **Post-handler order:** post-hook order differs between commands and queries/events.
- ✅ **Fixed. NotFoundException:** `NotFoundException(string, Exception)` had error code 0, not 404.
