# Changelog

[Magyar migrációs jegyzet](docs/hu/migration/from-original.md) | [English migration notes](docs/en/migration/from-original.md)

## 0.2.0-beta.12 (unreleased)

> **Not for production use.** This project is published exclusively for testing purposes. It comes without any
> warranty or support, and it may change or be withdrawn at any time. Do not use it in production systems.

First release of the rewrite as `Sarkany.ServiceComponents.*`, compatible with the original `ServiceComponents.*` 0.2.0-beta.12 and carrying its version number (see [switching packages](docs/en/migration/from-original.md#switching-packages) and [versioning](docs/en/versioning.md)); the assemblies and namespaces stay `ServiceComponents.*`. A rewrite of the internals. The public API covers the original's (checked with the PublicAPI analyzers and ApiCompat, `build/upstream-check.sh`), and the [wire protocol](docs/en/reference/wire-protocol.md) is unchanged: services running the original packages and services running these interoperate over HTTP and RabbitMQ in both directions (`build/interop.sh`). Every change below is detailed, with the test that pins it, in the [migration notes](docs/en/migration/from-original.md).

### Fixed (compatible with peers running the original packages)

- **Core, Domain:** `ValueObject` equality and hash code, `TimeInterval` `Contains`/`DurationRateOf`/`Simplify`, `Enumeration.GetAll` with other static fields, `NotFoundException(string, Exception)` error code, `EventSourcedAggregateRoot` `Apply` lookup (#9, #10, #15).
- **Infrastructure:** typed dispatch instead of `dynamic`, so non-public handlers work (#21); event handlers inherit the correlation (#1); classes with several handler interfaces are registered once (#20); request constraint counting (#5); Prometheus metric helpers (#11); event recorder waits (#12); loopback without `ICorrelation` and with `RoutableEvent`.
- **RabbitMQ:** the vhost and TLS of the URI are kept (#2); query replies for `null` results, reply queue cleanup, optional timeout (#3); no reject after ack (#7); `AddRabbit` commands and the retry chain use the configured routing key (#8); `AddRabbit(Func…)` without optional builders (#6); `mandatory` and the `basicProperties` template of the event publisher.
- **AspNet:** the authorization behavior keeps the HTTP correlation (#4); `JsonExceptionMapper` handles empty and non-JSON error bodies (#13); `ErrorHandlingMiddleware` logs and respects a started response (#14); `StartupCallbacks` run (#19); `Accept` no longer piles up on shared clients; `RoutableEvent` over HTTP; `RequestBinder`; consumer health check with duplicate tags.
- **Integrations:** no root NHibernate session; `NHibernateEventPublisherBehavior` disposes asynchronously and publishes once; `CouchDbConfigurationSource` checks the status code.
- **Testing:** `UseTestSender` works; the Seq sink of `UseOutputHelper` is configurable.

### Security

- Payloads with an embedded `$type` may only name message types and types of the registered assemblies (`AddMediator`, `RequestParser.AllowTypesFrom`); anything else is rejected with HTTP 400. With a `domain-type` header or `Type` property, only commands, queries and events are instantiated.

### Added

- `RabbitMessagingOptions` (`ServiceComponents:RabbitMessagingOptions`): `QueryTimeoutInSeconds`, `PrefetchCount`, and the opt-in `ReplyOnQueryFailure`, which changes the wire and must only be enabled when every sender runs Sarkany.ServiceComponents.
- `RequestParser.AllowTypesFrom`, `AddRabbitRetry(…, routingKey)`, `NHibernateEventPublisherBehavior.DisposeAsync`, `UseTestSenderAt`.
- `ServiceComponents.Host` contains working status badges (`AddBadges`, `MapBadges`, `BadgeOptions`); the original package was never released.

### Changed

- Package versions are managed centrally (`src/Directory.Packages.props`); packages ship XML documentation and SourceLink.
- `ServiceComponents.Infrastructure` no longer depends on the `System.ComponentModel.Annotations` package.
- Performance: see [Performance](docs/en/performance.md). The full pipeline is about twice as fast with fewer allocations; deliberate costs (the `$type` allowlist check) are documented there.

### Removed

- Seven empty, never published projects (`*.HandlerContext`, `*.Http`, `*.Mediator`, `Infrastructure.Validation`).

### Not changed on purpose

These [known issues](docs/en/known-issues.md) stay, because fixing them would change behavior that peers running the original packages or existing services rely on: new ids for messages without an id constructor parameter (#16), the ignored result of `IPostHandleQuery.PostHandleAsync` (#17), concurrent event handlers in one scope (#18), the post-hook order, the `BackgroundTaskQueue` overflow policy, queries routed to the queue named like the exchange, and the RabbitMQ query RPC throughput (about 130 requests per second; a channel pool is planned as an optional improvement).

## Original releases

The original `ServiceComponents.*` packages, up to 0.2.0-beta.12, are released from [gsinka/servicecomponents](https://github.com/gsinka/servicecomponents).
