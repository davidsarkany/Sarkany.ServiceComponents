**Magyar** | [English](../en/integrations.md)

# Integrációk

## Redis (`ServiceComponents.Infrastructure.Redis`, a wireup a `ServiceComponents.AspNet`-ben)

```csharp
hostBuilder.AddRedis("localhost:6379");                     // IConnectionMultiplexer + IDatabase (0-s adatbázis)
hostBuilder.AddRedis(configuration => configuration["Redis"]);
hostBuilder.AddRedisDistributedCache("localhost:6379", instance: "orders:");          // IDistributedCache
hostBuilder.AddRedisDistributedCache("localhost:6379,password=…");                    // feldolgozott ConfigurationOptions
hostBuilder.AddRedisDistributedCache(options => options.EndPoints.Add("redis", 6379));

builder.AddRedisConnection("localhost:6379", key: "cache");
builder.AddRedisDatabase(database: 2, connectionKey: "cache", key: "sessions");
```

A kapcsolat az `IConnectionMultiplexer` első feloldásakor nyílik meg. A request constraintekhez (`AddRequestConstraints`) `IDistributedCache` szükséges.

## NHibernate (`ServiceComponents.Infrastructure.NHibernate`)

```csharp
hostBuilder.AddNHibernate(
    configuration => configuration.GetConnectionString("orders"),
    map => map.FluentMappings.AddFromAssemblyOf<OrderMap>(),
    configuration => new SchemaUpdate(configuration).Execute(true, true));
```

Ezeket regisztrálja:

| Szolgáltatás | Élettartam |
|---|---|
| `Configuration` (FluentNHibernate, `PostgreSQLConfiguration.PostgreSQL82`) | singleton |
| `ISessionFactory` | singleton |
| `ISessionFactory` | singleton, **automatikusan aktiválva** |
| `ISession` | lifetime scope-onként, az első használatkor nyílik meg |

- **Indulás:** a container felépítésekor létrejön a session factory, ehhez elérhető PostgreSQL kell. A hibás connection string az eredetihez hasonlóan már induláskor kiderül.
- **Sessionök (Sarkany):** a session a scope-on belüli első használatkor nyílik meg. Az eredeti automatikusan aktiválta a sessiont, ezért egy root session az alkalmazás teljes élettartamáig nyitva maradt.

Az `NHibernateEventPublisherBehavior` (outbox jellegű publisher) minden publikált eventet `UnpublishedEventEntity`-ként ment a sessionbe, és a dispose-kor továbbadja az eventeket a következő publishernek. Egyik extension metódus sem regisztrálja, decoratorként kell regisztrálni. A Sarkany változatban az `IAsyncDisposable`-t is megvalósítja, így a scope `await using`-gal történő lezárása nem blokkol, és minden eventet csak egyszer ad tovább.

## CouchDB konfiguráció (`ServiceComponents.Infrastructure.CouchDB`)

```csharp
configurationBuilder.Add(new CouchDbConfigurationSource(new ServiceConfigurationOptions {
    Uri = new Uri("http://couchdb:5984/"), Database = "config", Document = "orders"
}));
```

A forrás `GET {Uri}{Database}/{Document}` kéréssel lekéri a dokumentumot, eltávolítja belőle az `_id` és a `_rev` mezőt, a többit pedig JSON konfigurációként tölti be.

Nem sikeres státuszkód esetén (például ha a dokumentum nem létezik) `InvalidOperationException`-t dob az útvonallal és a státusszal (Sarkany; az eredeti a hibadokumentumot töltötte be konfigurációként).

## Státusz badge-ek (`ServiceComponents.Host`)

SVG badge-ek dashboardokhoz és README-khez, shields.io stílusban:

```csharp
hostBuilder
    .AddHealthCheck((configuration, checks) => { })
    .AddBadges(badges => {
        badges.RegistrateVersionBadge();       // a belépési assembly informational verziója
        badges.RegistrateLivenessBadge();
        badges.RegistrateReadinessBadge();
        badges.RegistrateBadge("queue", "Queue", () => Task.FromResult("ok"), "#4c1");
    })
    .AddEndpoints();
```

| Kérés | Válasz |
|---|---|
| `GET /.badge/{name}` | `image/svg+xml` badge; ismeretlen névre szürke „Badge not found!” badge |
| `GET /.badge` | `text/html` az összes badge-dzsel |

- Az útvonal a `BadgeOptions.Path`, a `ServiceComponents:BadgeOptions` sectionből olvasva (alapértelmezés: `.badge`).
- A liveness és a readiness badge ugyanúgy választja ki a checkeket, mint a `/.well-known/live` és a `/.well-known/ready` endpoint: a tag nélküli és az adott taggel ellátott checkeket.
- `AddBadges` nélkül a `ContainerBuilder.AddBadgeService(...)` és az `IEndpointRouteBuilder.MapBadges(path)` használható; ilyenkor a `ServiceComponents.Host` assemblyt MVC application partként kell hozzáadni.
- Az eredeti repó a `ServiceComponents.Host` csomagot soha nem adta ki; a badge-ek a Sarkany változatban jelennek meg.
