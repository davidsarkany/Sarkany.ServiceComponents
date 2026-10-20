[Magyar](../hu/configuration.md) | **English**

# Configuration

The library binds only one configuration section itself: `ServiceComponents:RabbitConnectionOptions`.

The other options classes are resolved as `IOptions<T>` and fall back to their defaults unless your application binds them, for example `services.Configure<HttpRequestOptions>(configuration.GetSection("httpRequest"))`.

## Options classes

**`ServiceComponents.Infrastructure.Rabbit.RabbitConnectionOptions`**

Section: `ServiceComponents:RabbitConnectionOptions` (bound automatically).

| Property | Default |
|---|---|
| `EndpointUri` | required |
| `ClientName` | required |
| `AutomaticRecoveryEnabled` | `true` |
| `HandshakeContinuationTimeoutInSeconds` | `120` |
| `RequestedHeartbeatInSeconds` | `60` |
| `RequestedChannelMaxInSeconds` | `2048` |
| `NetworkRecoveryIntervalInSeconds` | `5` |
| `Ssl` | `false` |
| `VirtualHost` | `/` |

**`ServiceComponents.AspNet.Http.HttpRequestOptions`**

Not bound by the library; register it yourself.

| Property | Default |
|---|---|
| `DomainTypeHeaderKey` | `domain-type` |
| `CorrelationIdHeaderKey` | `correlation-id` |
| `CausationIdHeaderKey` | `causation-id` |

**`ServiceComponents.Infrastructure.Receivers.CorrelationLogOptions`**

Not bound by the library; register it yourself.

| Property | Default |
|---|---|
| `CorrelationIdPropertyName` | `correlation-id` |
| `CausationIdPropertyName` | `causation-id` |
| `CommandIdPropertyName` | `command-id` |
| `QueryIdPropertyName` | `query-id` |
| `EventIdPropertyName` | `event-id` |

**`ServiceComponents.Infrastructure.Options.ApplicationOptions`**

Not bound by the library; register it yourself.

| Property | Default |
|---|---|
| `Name` | `[Required]` |
| `ShortName` | `[Required]` |
| `Description` | |

Static members, read from the entry assembly:

| Member | Value |
|---|---|
| `InformationalVersion` | Informational version |
| `FullSemVer` | Informational version up to `+` |
| `Version` | File version |
| `ApiVersion` | File version major |

**`ServiceComponents.AspNet.OpenApi.OpenApiOptions`**

Not bound by the library; register it yourself.

| Property | Default |
|---|---|
| `DocumentName` | `Document` |
| `Path` | `/swagger` |
| `AppName` | `APP` |
| `ClientId` | `ClientId` |

**`ServiceComponents.AspNet.OpenApi.OpenApiDocumentOptions`**

Not bound by the library; register it yourself.

| Property | Default |
|---|---|
| `DocumentName` | `v0.1` |
| `AppTitle` | `Title` |
| `AppVersion` | `0.0.1` |

**`ServiceComponents.AspNet.OpenApi.OpenApiSsoOptions`**

Not bound by the library; register it yourself.

| Property | Default |
|---|---|
| `SecuritySchemeName` | `OAuth2` |
| `SecurityScheme` | `http` |
| `SecuritySchemeType` | `OAuth2` |
| `OAuthFlow` | `Implicit` |
| `Authority` | `http://localhost` |

**`ServiceComponents.AspNet.SSO.JwtTokenValidationOptions`**

Not bound by the library; register it yourself.

| Property | Default |
|---|---|
| `Authority` | |
| `Audience` | `account` |
| `RequireHttpsMetadata` | `true` |
| `SaveToken` | `true` |
| `ShowPII` | `false` |

**`ServiceComponents.Infrastructure.CouchDB.ServiceConfigurationOptions`**

Passed to `CouchDbConfigurationSource`.

| Property | Default |
|---|---|
| `Uri` | |
| `Database` | |
| `Document` | |

**`ServiceComponents.Infrastructure.Rabbit.RabbitMessagingOptions`** (Sarkany)

Section `ServiceComponents:RabbitMessagingOptions`, or a registered instance. See [RabbitMQ](transports/rabbitmq.md#messaging-options-sarkany).

| Property | Default |
|---|---|
| `QueryTimeoutInSeconds` | none |
| `PrefetchCount` | `0` (no `basic.qos`) |
| `ReplyOnQueryFailure` | `false` |

**`ServiceComponents.Host.Badge.BadgeOptions`** (Sarkany)

Section `ServiceComponents:BadgeOptions`, read by `AddBadges`.

| Property | Default |
|---|---|
| `Path` | `.badge` |

**Testing:** `ServiceComponents:Testing:SeqServerUrl` sets the Seq sink of `UseOutputHelper` (default `http://localhost:5341`, `""` disables it; Sarkany).

## Other settings

These are passed as method arguments rather than read from configuration.

- **`AddRabbit`:** connection string, client name, queue, exchange, routing key, retry intervals.
- **`AddHttpSender`:** endpoint URI, sender key.
- **`AddHealthCheck`:** readiness path, liveness path.
- **`AddRedis` / `AddRedisDistributedCache`:** connection string.
- **`AddNHibernate`:** connection string, mapping, schema callback.
- **`EventRecorderService`:** keeps events for 60 000 ms by default.

> **Arrays and the configuration binder.** `ConfigurationBinder` appends to arrays that already have a value instead of replacing them. Do not give array options (for example retry intervals) a default value in the class you bind; apply the default after binding. The sample's `RabbitSettings` shows how.
