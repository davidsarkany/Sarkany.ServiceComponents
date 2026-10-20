**Magyar** | [English](../en/configuration.md)

# Konfiguráció

A könyvtár magától csak egy konfigurációs sectiont köt be: a `ServiceComponents:RabbitConnectionOptions`-t.

A többi Options osztályt `IOptions<T>`-ként oldja fel. Ha az alkalmazás nem köti be őket, az alapértékek érvényesek. Bekötés például: `services.Configure<HttpRequestOptions>(configuration.GetSection("httpRequest"))`.

## Options osztályok

**`ServiceComponents.Infrastructure.Rabbit.RabbitConnectionOptions`**

Section: `ServiceComponents:RabbitConnectionOptions` (automatikusan kötődik).

| Property | Alapérték |
|---|---|
| `EndpointUri` | kötelező |
| `ClientName` | kötelező |
| `AutomaticRecoveryEnabled` | `true` |
| `HandshakeContinuationTimeoutInSeconds` | `120` |
| `RequestedHeartbeatInSeconds` | `60` |
| `RequestedChannelMaxInSeconds` | `2048` |
| `NetworkRecoveryIntervalInSeconds` | `5` |
| `Ssl` | `false` |
| `VirtualHost` | `/` |

**`ServiceComponents.AspNet.Http.HttpRequestOptions`**

Section: a könyvtár nem köti be, neked kell.

| Property | Alapérték |
|---|---|
| `DomainTypeHeaderKey` | `domain-type` |
| `CorrelationIdHeaderKey` | `correlation-id` |
| `CausationIdHeaderKey` | `causation-id` |

**`ServiceComponents.Infrastructure.Receivers.CorrelationLogOptions`**

Section: a könyvtár nem köti be, neked kell.

| Property | Alapérték |
|---|---|
| `CorrelationIdPropertyName` | `correlation-id` |
| `CausationIdPropertyName` | `causation-id` |
| `CommandIdPropertyName` | `command-id` |
| `QueryIdPropertyName` | `query-id` |
| `EventIdPropertyName` | `event-id` |

**`ServiceComponents.Infrastructure.Options.ApplicationOptions`**

Section: a könyvtár nem köti be, neked kell.

| Property | Alapérték |
|---|---|
| `Name` | `[Required]` |
| `ShortName` | `[Required]` |
| `Description` | |

Statikus tagok, az entry assembly-ből:

| Tag | Érték |
|---|---|
| `InformationalVersion` | az informational verzió |
| `FullSemVer` | az informational verzió a `+` jelig |
| `Version` | a fájlverzió |
| `ApiVersion` | a fájlverzió főverziója |

**`ServiceComponents.AspNet.OpenApi.OpenApiOptions`**

Section: a könyvtár nem köti be, neked kell.

| Property | Alapérték |
|---|---|
| `DocumentName` | `Document` |
| `Path` | `/swagger` |
| `AppName` | `APP` |
| `ClientId` | `ClientId` |

**`ServiceComponents.AspNet.OpenApi.OpenApiDocumentOptions`**

Section: a könyvtár nem köti be, neked kell.

| Property | Alapérték |
|---|---|
| `DocumentName` | `v0.1` |
| `AppTitle` | `Title` |
| `AppVersion` | `0.0.1` |

**`ServiceComponents.AspNet.OpenApi.OpenApiSsoOptions`**

Section: a könyvtár nem köti be, neked kell.

| Property | Alapérték |
|---|---|
| `SecuritySchemeName` | `OAuth2` |
| `SecurityScheme` | `http` |
| `SecuritySchemeType` | `OAuth2` |
| `OAuthFlow` | `Implicit` |
| `Authority` | `http://localhost` |

**`ServiceComponents.AspNet.SSO.JwtTokenValidationOptions`**

Section: a könyvtár nem köti be, neked kell.

| Property | Alapérték |
|---|---|
| `Authority` | |
| `Audience` | `account` |
| `RequireHttpsMetadata` | `true` |
| `SaveToken` | `true` |
| `ShowPII` | `false` |

**`ServiceComponents.Infrastructure.CouchDB.ServiceConfigurationOptions`**

A `CouchDbConfigurationSource` kapja meg paraméterként.

| Property | Alapérték |
|---|---|
| `Uri` | |
| `Database` | |
| `Document` | |

**`ServiceComponents.Infrastructure.Rabbit.RabbitMessagingOptions`** (0.3)

Section: `ServiceComponents:RabbitMessagingOptions`, vagy egy regisztrált példány. Lásd [RabbitMQ](transports/rabbitmq.md#üzenetkezelési-beállítások-03).

| Property | Alapérték |
|---|---|
| `QueryTimeoutInSeconds` | nincs |
| `PrefetchCount` | `0` (nincs `basic.qos`) |
| `ReplyOnQueryFailure` | `false` |

**`ServiceComponents.Host.Badge.BadgeOptions`** (0.3)

Section: `ServiceComponents:BadgeOptions`, az `AddBadges` olvassa.

| Property | Alapérték |
|---|---|
| `Path` | `.badge` |

**Testing:** a `ServiceComponents:Testing:SeqServerUrl` a `UseOutputHelper` Seq sinkjét állítja (alapértelmezés: `http://localhost:5341`, `""` kikapcsolja; 0.3).

## Egyéb beállítások

Ezeket metódusparaméterként kell átadni:

- `AddRabbit`: connection string, kliensnév, queue, exchange, routing key, retry intervallumok.
- `AddHttpSender`: endpoint URI, sender kulcs.
- `AddHealthCheck`: readiness és liveness útvonal.
- `AddRedis` / `AddRedisDistributedCache`: connection string.
- `AddNHibernate`: connection string, mapping, séma callback.

Az `EventRecorderService` alapból 60 000 ms-ig őrzi az eventeket.

> **Tömbök és a configuration binder.** A `ConfigurationBinder` a már értékkel rendelkező tömbökhöz hozzáfűz, nem cseréli le őket. Ne adj alapértéket a bekötött osztály tömb típusú beállításainak (például a retry intervallumoknak), hanem a bekötés után állítsd be az alapértéket. Ezt mutatja a példaprojekt `RabbitSettings` osztálya.
