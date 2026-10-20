[Magyar](../../hu/migration/from-original.md) | **English**

# Migrating from the original packages

Sarkany.ServiceComponents ("Sarkany" below) is a rewrite of the internals of the original `ServiceComponents.*` packages ("original" below). It keeps the public API (source compatible) and the [wire protocol](../reference/wire-protocol.md) unchanged: switching needs no code changes, and services running the original packages and services running Sarkany interoperate over HTTP and RabbitMQ.

## Switching packages

Sarkany is published under new package ids, with the version number of the original release it replaces (see [Versioning](../versioning.md)); the assemblies inside keep their names and namespaces.

| Original package | Sarkany package |
|---|---|
| `ServiceComponents.Api` | `Sarkany.ServiceComponents.Api` |
| `ServiceComponents.Core` | `Sarkany.ServiceComponents.Core` |
| `ServiceComponents.Domain` | `Sarkany.ServiceComponents.Domain` |
| `ServiceComponents.Application` | `Sarkany.ServiceComponents.Application` |
| `ServiceComponents.Infrastructure` | `Sarkany.ServiceComponents.Infrastructure` |
| `ServiceComponents.Infrastructure.Rabbit` | `Sarkany.ServiceComponents.Infrastructure.Rabbit` |
| `ServiceComponents.Infrastructure.Redis` | `Sarkany.ServiceComponents.Infrastructure.Redis` |
| `ServiceComponents.Infrastructure.NHibernate` | `Sarkany.ServiceComponents.Infrastructure.NHibernate` |
| `ServiceComponents.Infrastructure.CouchDB` | `Sarkany.ServiceComponents.Infrastructure.CouchDB` |
| `ServiceComponents.AspNet` | `Sarkany.ServiceComponents.AspNet` |
| `ServiceComponents.Host` | `Sarkany.ServiceComponents.Host` |
| `ServiceComponents.Testing` | `Sarkany.ServiceComponents.Testing` |

- Replace the ids in the `PackageReference`s (or in `Directory.Packages.props`) and keep the version: e.g. `ServiceComponents.AspNet` 0.2.0-beta.12 becomes `Sarkany.ServiceComponents.AspNet` 0.2.0-beta.12. No code changes. Packages that come transitively (e.g. `Core` through `AspNet`) switch with their parent.
- A project must not reference an original and a new package at the same time, also not transitively through another library: both contain e.g. `ServiceComponents.Core.dll`. Switch shared libraries together with the services that use them.
- Supported original releases: `0.2.0-beta.12`, and the earlier `0.2.1-beta.1` (built from an older commit, it lacks `ProcessContextBehavior`). Its assemblies carry version 0.2.1.0, so binaries compiled against `0.2.1-beta.1` must be rebuilt; switching the packages does that anyway.
- `ServiceComponents.Host` was never released by the original repository; `Sarkany.ServiceComponents.Host` is new.
- Services running the original packages and services running these interoperate over HTTP and RabbitMQ, so services can be switched one at a time.

## Compatible fixes

These fix [known issues](../known-issues.md) without affecting services running the original packages.

### ServiceComponents.Core

| Change | Known issue | Test |
|---|---|---|
| `ValueObject.Equals` compares the full value sequence: objects with a different number of values are no longer equal, in either direction. | #9 | `ValueObjectTests.Different_value_counts_are_not_equal` |
| `ValueObject.GetHashCode` no longer throws for an empty value list, and hashes the values in order. | #9 | `ValueObjectTests.Empty_value_object_has_a_hash_code` |
| `TimeIntervalExtensions.Contains(DateTime)` returns true for points inside the half-open interval [Start, Finish). It used to return true for points before it. | #10 | `TimeIntervalTests.Contains_datetime_of_half_open_interval` |
| `TimeIntervalExtensions.DurationRateOf` returns the real fraction instead of an integer division result. | #10 | `TimeIntervalTests.DurationRateOf_is_a_fraction` |
| `TimeIntervalExtensions.Simplify` always merges touching intervals and returns them ordered by start. | #10 | `TimeIntervalTests.Simplify_merges_touching_intervals_regardless_of_order` |
| `Enumeration.GetAll` ignores static fields of other types instead of failing. | minor | `EnumerationTests.Other_static_fields_are_ignored` |
| `NotFoundException(string, Exception)` sets error code 404, like the other constructors. | minor | `CoreMiscTests.Derived_business_exceptions` |
| `StringHelper.Random` uses `RandomNumberGenerator.GetString` (uniform, no modulo bias). | | |

### ServiceComponents.Domain

| Change | Known issue | Test |
|---|---|---|
| A missing `Apply` method is detected before the version is incremented or the event is recorded. | #15 | `EventSourcedAggregateRootTests.Missing_apply_method_throws_without_changing_the_aggregate` |
| Non-public `Apply` methods of base classes are found. | #15 | `EventSourcedAggregateRootTests.Private_apply_methods_of_base_classes_are_found` |
| The `Apply` method lookup is cached per aggregate and event type. | | |

These stay as before:

- the version is incremented before `Apply` runs;
- exceptions thrown by `Apply` arrive wrapped in `TargetInvocationException`.

### ServiceComponents.Infrastructure

| Change | Known issue | Test |
|---|---|---|
| Dispatchers, hooks and validation call handlers through cached, strongly typed invokers instead of `dynamic`. Non-public handlers and hooks work. | #21 | `MediatorPipelineTests.Non_public_handler_is_dispatched` |
| Event handlers inherit the correlation (`CorrelationId`, `CausationId`, `CurrentId`) of the dispatching scope; events they publish continue the chain. | #1 | `LoopbackAndRoutingTests`, `RabbitConsumerTests.Event_is_dispatched_and_acked`, `HttpReceiverContractTests.Event_is_received` |
| `AddMediator` registers a class once even when it implements several handler interfaces (it used to handle each event three times). | #20 | `MediatorPipelineTests.Class_with_several_handler_interfaces_is_registered_once` |
| Request constraints: correct decrement (the key is removed at zero), no exception on repeated completion, earlier keys are rolled back when a later key rejects, a nested request's rejection no longer leaks the outer request's slot, cleanup ignores a cancelled token and never hides the original exception. | #5 | `BehaviorTests.Constraint_*` |
| Prometheus: reflection results are cached per metric class; a class without a metric attribute gets a clear `InvalidOperationException`; null label values become empty strings; `Observe(value, count)` no longer fails on classes with properties and uses exponential buckets for exponential histograms. | #11 | `MonitoringTests` |
| Event recorder: no lost wake-up between lookup and registration, cancelled waiters are removed, waiters for a base type receive live events. A timed-out wait still returns `default`. | #12 | `EventRecorderTests` |
| Loopback: works without a registered `ICorrelation` (starts a new chain); a `RoutableEvent` is unwrapped and delivered to the handlers of the inner event. | minor | `LoopbackAndRoutingTests`, `KnownDefectsTests.Routable_event_is_unwrapped_by_loopback` |
| `RequestParser`: payloads with an embedded `$type` may only name message types, types of the assemblies passed to `AddMediator` or `RequestParser.AllowTypesFrom`, types declared in a message's assembly, and plain .NET value and collection types; anything else is rejected with `InvalidOperationException` (HTTP 400). With a type name, types that are not commands, queries or events are no longer instantiated. Type names are cached. | security | `RequestParserTests` |

New API (additive): `RequestParser.AllowTypesFrom(params Assembly[])`.

### ServiceComponents.Infrastructure.Rabbit and AddRabbit

| Change | Known issue | Test |
|---|---|---|
| The connection keeps the virtual host and TLS settings of the URI (`amqps://host/vhost`). `VirtualHost` and `Ssl` only apply when set to non-default values. | #2 | `KnownDefectsTests.Rabbit_connection_factory_keeps_vhost_and_tls_from_uri` |
| Query replies are awaited asynchronously instead of blocking a thread-pool thread. The reply queue is deleted also on failure and cancellation. Throughput is unchanged; see [Performance](../performance.md). | #3 | `RabbitSenderTests.Query_without_reply_waits_until_cancelled_and_cleans_up` |
| A query whose handler returns `null` is answered with `null`. The caller used to wait forever; original senders also read the reply as `null`. | #3 | `RabbitConsumerTests.Null_query_result_is_answered_with_null` |
| A failure while sending the query reply after the acknowledgement is logged; the delivery is no longer rejected a second time. | #7 | `KnownDefectsTests.Rabbit_consumer_does_not_reject_after_ack_when_reply_fails` |
| `AddRabbit` sends commands with the configured routing key, the one the queue is bound with. | #8 | `RabbitWireupTests.AddRabbit_commands_and_retry_bindings_use_the_routing_key`, integration `Commands_arrive_with_a_non_empty_routing_key` |
| The retry chain is also bound with the configured routing key, so dead-lettered messages that keep that key are retried instead of dropped. Bindings are additive, so services running the original packages can declare the same topology. New overload: `AddRabbitRetry(channel, scope, queue, ttls, routingKey)`. | #8 | integration `Routed_events_survive_the_retry_chain` |
| `AddRabbit(Func…)` accepts omitted routing key and retry interval builders. | #6 | `RabbitWireupTests.AddRabbit_configuration_overload_without_optional_builders` |
| `AddRabbitEventPublisher` honours `mandatory` and uses `basicProperties` as a template for every message. | minor | `RabbitSenderTests.Event_publisher_uses_mandatory_and_basic_properties_template` |
| The consumer receives queries through a typed invoker instead of `dynamic`. | | |

Not changed:

- queries still go to the queue named like the exchange;
- `RoutableEvent`s with other routing keys than the configured one are still not retried.

### ServiceComponents.AspNet

| Change | Known issue | Test |
|---|---|---|
| `AddHttpReceiverAuthorizationBehavior` decorates the HTTP receiver chain, so the HTTP correlation behavior keeps running. The handler class is read from its registration instead of resolving the handler; lambda registrations are still resolved. The public constructors of the behaviors are unchanged. | #4 | `HostingTests.Authorized_caller_keeps_http_correlation`, `AuthorizationBehaviorTests` |
| `JsonExceptionMapper` maps empty, non-JSON and untyped error bodies by status code (401, 404, 501, 400) instead of throwing `JsonReaderException` or `ArgumentNullException`. The response content is buffered before mapping, so the default mapper no longer blocks on it. | #13 | `HttpSenderContractTests.Non_json_or_untyped_error_bodies_map_by_status_code`, `KnownDefectsTests.Exception_mapper_maps_empty_401_body` |
| `ErrorHandlingMiddleware` logs failures mapped to 5xx at `Error`, others at `Debug`. When the response has already started it logs and rethrows instead of failing while writing. Status codes and bodies are unchanged. | #14 | `KnownDefectsTests.Error_middleware_*` |
| `ServiceComponentsHostBuilder` invokes `StartupCallbacks` (registered with `RegisterCallback(Action<IConfiguration, ILifetimeScope>)`) with the root scope once the container is built, before the application builder callbacks. | #19 | `HostingTests.Startup_callbacks_are_invoked_with_the_root_scope` |
| The HTTP sender sets `Accept` per request; it no longer piles up on a shared `HttpClient`. Headers without a value are skipped (they never reached the wire). | minor | `HttpSenderContractTests.Reused_client_does_not_accumulate_accept_header` |
| The HTTP event publisher posts the inner event of a `RoutableEvent`. | minor | `HttpSenderContractTests.Routable_event_is_posted_as_the_wrapped_event` |
| `RequestBinder` leaves the result `Failed()` for non-request types. | minor | `HostingTests.Request_binder_fails_for_non_request_types` |
| `RabbitMQConsumerChecker` accepts duplicate and missing consumer tags; an unhealthy consumer is never hidden by a healthy one with the same tag. | minor | `KnownDefectsTests.Consumer_health_check_tolerates_duplicate_and_missing_tags` |

> **Behavior to check:** a `StartupCallbacks` entry registered with the original packages never ran; with Sarkany it runs at startup.

### Integrations

| Change | Known issue | Test |
|---|---|---|
| `NhibernateModule` auto-activates the session factory instead of a session: no root `ISession` stays open; a wrong connection string still fails at startup. | minor | `IntegrationPackageTests.NHibernate_module_builds_the_session_factory_on_container_build` |
| `NHibernateEventPublisherBehavior` implements `IAsyncDisposable` and forwards each event once, also when disposed again. | minor | `IntegrationPackageTests.NHibernate_event_publisher_*` |
| `CouchDbConfigurationSource` throws `InvalidOperationException` for non-success responses instead of loading the error document. | minor | `IntegrationPackageTests.CouchDb_configuration_source_rejects_error_responses` |
| `ServiceComponents.Host` contains the status badges (`AddBadges`, `AddBadgeService`, `MapBadges`, `BadgeOptions.ConfigurationSectionName`). The original package was never released, so this is additive. Its sources for a metrics controller and a request binder were duplicates of `ServiceComponents.AspNet` and are removed. The badges are drawn without System.Drawing, so the package no longer depends on DotBadge and the vulnerable `System.Drawing.Common` 4.6.0, and works on Linux. | | `BadgeTests` |

New API (additive): `NHibernateEventPublisherBehavior.DisposeAsync()` and the `ServiceComponents.Host` API above.

### ServiceComponents.Testing

| Change | Known issue | Test |
|---|---|---|
| `TestHost<T>` without an output helper (e.g. created by `UseTestSender`) starts without the test output sink instead of throwing. `UseOutputHelper(null)` still throws. | minor | `samples/Orders.Tests/TestSenderTests` |
| `UseTestSender` posts to `/api/generic` (it posted to the host root) and disposes the test host with the container. `UseTestSenderAt(requestUri, key)` selects another path. | minor | `samples/Orders.Tests/TestSenderTests` |
| The Seq sink of `UseOutputHelper` can be redirected or disabled with `ServiceComponents:Testing:SeqServerUrl`; the default stays `http://localhost:5341`. | minor | `TestingPackageTests.UseOutputHelper_writes_to_the_output_helper_with_configurable_seq` |

New API (additive): `ContainerBuilderExtensions.UseTestSenderAt<TTestHost>(requestUri, key)`.



## Opt-in changes

These change the behavior on the wire and are off by default. Enable them only when every peer runs Sarkany.ServiceComponents.

`RabbitMessagingOptions` (section `ServiceComponents:RabbitMessagingOptions`, or a registered instance):

| Option | Default | Effect |
|---|---|---|
| `ReplyOnQueryFailure` | `false` | When a query handler fails, the consumer also sends an error reply (headers `x-sc-error-kind`, `x-sc-error-message`, `x-sc-error-code`). The Sarkany sender throws the matching exception instead of waiting. **Original senders would read the error reply as a `null` result**, so enable it only when all senders run Sarkany.ServiceComponents. |
| `QueryTimeoutInSeconds` | none | The sender fails with `TimeoutException` after this time. This is local only, so it is safe with peers running the original packages. |
| `PrefetchCount` | `0` | `basic.qos` limit per consumer. This is local only, so it is safe with peers running the original packages. |

## Package dependencies

- `ServiceComponents.Infrastructure` no longer depends on the `System.ComponentModel.Annotations` package; .NET 10 ships the assembly.
- All other package dependencies and their versions are unchanged. Versions are now managed centrally in `src/Directory.Packages.props`.
- The packages now contain XML documentation files.

## Removed

The empty projects below were never published and are removed:

- `ServiceComponents.Application.HandlerContext`
- `ServiceComponents.Application.Http`
- `ServiceComponents.Application.Mediator`
- `ServiceComponents.Infrastructure.HandlerContext`
- `ServiceComponents.Infrastructure.Http`
- `ServiceComponents.Infrastructure.Mediator`
- `ServiceComponents.Infrastructure.Validation`
