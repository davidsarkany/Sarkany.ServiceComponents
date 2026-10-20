[Magyar](../hu/integrations.md) | **English**

# Integrations

## Redis (`ServiceComponents.Infrastructure.Redis`, wireup in `ServiceComponents.AspNet`)

```csharp
hostBuilder.AddRedis("localhost:6379");                     // IConnectionMultiplexer + IDatabase (db 0)
hostBuilder.AddRedis(configuration => configuration["Redis"]);
hostBuilder.AddRedisDistributedCache("localhost:6379", instance: "orders:");          // IDistributedCache
hostBuilder.AddRedisDistributedCache("localhost:6379,password=…");                    // parsed ConfigurationOptions
hostBuilder.AddRedisDistributedCache(options => options.EndPoints.Add("redis", 6379));

builder.AddRedisConnection("localhost:6379", key: "cache");
builder.AddRedisDatabase(database: 2, connectionKey: "cache", key: "sessions");
```

The connection is opened when `IConnectionMultiplexer` is first resolved.

`IDistributedCache` is needed by the request constraints (`AddRequestConstraints`).

## NHibernate (`ServiceComponents.Infrastructure.NHibernate`)

```csharp
hostBuilder.AddNHibernate(
    configuration => configuration.GetConnectionString("orders"),
    map => map.FluentMappings.AddFromAssemblyOf<OrderMap>(),
    configuration => new SchemaUpdate(configuration).Execute(true, true));
```

Registers:

| Service | Lifetime |
|---|---|
| `Configuration` | singleton (FluentNHibernate, `PostgreSQLConfiguration.PostgreSQL82`) |
| `ISessionFactory` | singleton |
| `ISessionFactory` | singleton, **auto-activated** |
| `ISession` | per lifetime scope, opened on first use |

- **Startup:** building the container builds the session factory, which requires a reachable PostgreSQL. A wrong connection string fails at startup, as in the original.
- **Sessions (Sarkany):** sessions are opened on first use in a scope. The original auto-activated the session, so a root session stayed open for the application's lifetime.

`NHibernateEventPublisherBehavior` (outbox-style publisher) stores each published event as an `UnpublishedEventEntity` in the session and forwards the events to the next publisher when it is disposed. It is not registered by any extension; register it yourself as a decorator. In Sarkany, it also implements `IAsyncDisposable`, so disposing the scope with `await using` does not block, and each event is forwarded once.

## CouchDB configuration (`ServiceComponents.Infrastructure.CouchDB`)

```csharp
configurationBuilder.Add(new CouchDbConfigurationSource(new ServiceConfigurationOptions {
    Uri = new Uri("http://couchdb:5984/"), Database = "config", Document = "orders"
}));
```

The source reads the document with `GET {Uri}{Database}/{Document}`, removes `_id` and `_rev`, and loads the rest as JSON configuration.

A non-success status code (e.g. a missing document) throws `InvalidOperationException` with the path and status (Sarkany; the original loaded the error document as configuration).

## Status badges (`ServiceComponents.Host`)

SVG badges for dashboards and READMEs, in the shields.io style:

```csharp
hostBuilder
    .AddHealthCheck((configuration, checks) => { })
    .AddBadges(badges => {
        badges.RegistrateVersionBadge();       // informational version of the entry assembly
        badges.RegistrateLivenessBadge();
        badges.RegistrateReadinessBadge();
        badges.RegistrateBadge("queue", "Queue", () => Task.FromResult("ok"), "#4c1");
    })
    .AddEndpoints();
```

| Request | Response |
|---|---|
| `GET /.badge/{name}` | `image/svg+xml` badge; an unknown name gives a grey "Badge not found!" badge |
| `GET /.badge` | `text/html` with all badges |

- The path is `BadgeOptions.Path`, read from `ServiceComponents:BadgeOptions` (default `.badge`).
- The liveness and readiness badges select checks like the `/.well-known/live` and `/.well-known/ready` endpoints: untagged checks and checks with the tag.
- Without `AddBadges`, use `ContainerBuilder.AddBadgeService(...)` and `IEndpointRouteBuilder.MapBadges(path)`, and add the `ServiceComponents.Host` assembly as an MVC application part.
- The original repository never released `ServiceComponents.Host`; the badges are new in Sarkany.
