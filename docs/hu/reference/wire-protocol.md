**Magyar** | [English](../../en/reference/wire-protocol.md)

# Wire protocol referencia

Ez az oldal **normatív**: azt írja le, amire más szolgáltatások, dashboardok és konfigurációk építenek. A ServiceComponents minden verziójának változatlanul meg kell tartania, hogy a régi és az új szolgáltatások együtt tudjanak működni.

Minden pontot tesztek rögzítenek: `src/ServiceComponents.UnitTests` (U) vagy `src/ServiceComponents.IntegrationTests` (I).

## Üzenetek szerializálása (U: `Api/MessageTests`, `Infrastructure/RequestParserTests`)

- **Típusnév:** `"{Type.FullName}, {AssemblyName}"`. Nincs benne verzió, culture és public key token.
- **Body:** `JsonConvert.SerializeObject(message, Formatting.None)`, alapértelmezett Newtonsoft beállításokkal:
  - a property-nevek a deklarált alakban (PascalCase);
  - a null értékek is szerepelnek;
  - nincs `$type`.
- **Azonosítók:** a `CommandId` / `QueryId` / `EventId` csak getterrel rendelkezik. A `commandId` / `queryId` / `eventId` konstruktorparaméteren keresztül áll vissza; ha nincs megadva, új GUID keletkezik.

**Feldolgozás**

| Bemenet | Mi történik |
|---|---|
| Van típusnév | Deserializálás a `Type.GetType(typeName)` típusba, alapértelmezett beállításokkal |
| Nincs típusnév | `TypeNameHandling.All` és `TypeNameAssemblyFormatHandling.Simple`, tehát a `$type` kötelező |
| Üres body | `{}`-ként kezeli |
| Hibás JSON | a `""` string lesz belőle |
| Nem command, query vagy event | A nyers üzenetet fogadó callback kapja, ha van. Ha nincs: `InvalidOperationException("Unknown request type: <type> (not a command, query or event)")` |

## HTTP (U: `AspNet/HttpReceiverContractTests`, `AspNet/HttpSenderContractTests`)

| | Fogadó | Küldő |
|---|---|---|
| Endpoint | az alkalmazás határozza meg, megállapodás szerint `POST /api/generic` | a beállított URI, `POST` |
| Típus | `domain-type` header | `domain-type` header (content header) |
| Correlation | `correlation-id` header, hiányában GUID | `correlation-id`, ha be van állítva |
| Causation | `causation-id` header | `causation-id` = az aktuális azonosító, null esetén nem kerül ki |
| Body | nyers UTF-8, a content type nem számít | `text/plain; charset=utf-8` |

**Válaszok**

| Eset | Státusz | Body |
|---|---|---|
| Siker | 200 | üres, vagy a query eredménye |
| `UnauthorizedAccessException` | 401 | `{"Type":"security","ErrorMessage":m}` |
| `NotImplementedException` | 501 | `{"Type":"generic","ErrorMessage":m}` |
| `NotFoundException` | 404 | `{"Type":"generic","ErrorMessage":m}` |
| `BusinessException` | 400 | `{"Type":"business","ErrorCode":c,"ErrorMessage":m}` |
| `ValidationException` | 400 | `{"Type":"validation","Errors":[{"PropertyName","ErrorCode","ErrorMessage","AttemptedValue"}],"ErrorMessage":"Validation failed"}` |
| `InvalidOperationException` | 400 | `{"Type":"generic","ErrorMessage":m}` |
| minden más | 500 | `{"Type":"generic","ErrorMessage":"Something really bad happened"}` |

**Visszaképezés a küldő oldalon**

| Válasz | Kivétel |
|---|---|
| 401 | `UnauthorizedAccessException` |
| 501 | `NotImplementedException` |
| 404 | `NotFoundException` |
| 400 + business | `BusinessException(code, m)` |
| 400 + validation | `ValidationException` |
| egyéb 400 | `InvalidOperationException` |
| minden más | `Exception("Unknown error")` |

A Sarkany változatban az üres vagy nem hiba-JSON body-k csak a státuszkód alapján képződnek le (401, 404, 501 a fenti módon; a 400 a body szövegével `InvalidOperationException` lesz). A drótra kerülő body-k változatlanok.

## RabbitMQ (U: `Rabbit/*`, `AspNet/RabbitWireupTests`; I: `RabbitEndToEndTests`)

**Topológia**

- A topológia nevei és argumentumai pontosan azok, amelyek a [RabbitMQ transport](../transports/rabbitmq.md#topológia) oldalon szerepelnek.
- Minden objektum non-durable és auto-delete, az exchange-ek `direct` típusúak.
- Az `x-message-ttl` int típusú.

**Property-k**

| Property | Érték |
|---|---|
| `Type` | a típusnév |
| `CorrelationId` | mindig be van állítva |
| `MessageId` | az `EventId`, csak eventeknél |
| `ReplyTo` | csak queryknél |
| `causation_id` header | küldéskor string, fogadáskor `byte[]` |
| `ContentType`, `DeliveryMode`, időbélyeg | nincs beállítva |

**Routing**

| Üzenet | Exchange | Routing key |
|---|---|---|
| Command | a beállított exchange | a beállított kulcs (`AddRabbit` esetén `""`) |
| Event | a beállított exchange | `RoutableEvent.RoutingKey`, vagy a beállított kulcs |
| Query | `""` | a beállított exchange neve |

**RPC**

- A válasz a `ReplyTo` címre megy a default exchange-en keresztül. Csak a `CorrelationId` van beállítva, a body a JSON eredmény.
- A consumer előbb ackol, utána válaszol.
- Null eredmény: az eredeti nem válaszol; a Sarkany változat `null` body-val válaszol, amit az eredeti senderek is `null`-ként olvasnak.
- Hiba: alapból nincs válasz. Az opt-in `ReplyOnQueryFailure` (Sarkany) esetén a hibaválasz az `x-sc-error-kind`, `x-sc-error-message` és `x-sc-error-code` headereket viszi; az eredeti senderek ezt `null` eredményként olvasnák.

**Ack**

- Kézi ack.
- Bármilyen hiba esetén `BasicReject(requeue: false)`.
- Nincs QoS beállítva.

**Consumer tagek és Autofac kulcsok** (`AddRabbit`)

| Elem | Név |
|---|---|
| Consumer tagek | `{client}-consumer-{i}`, `{client}-consumer-retry-{ttl}` |
| Kapcsolatok kliensnevei | `{client}-generic`, `{client}-publisher`, `{client}-consumer` |
| Kulcsok | `publisher`, `consumer`, `consumer-{i}`, `consumer-retry`, `consumer-retry-{ttl}`, `__consumer__`, `rabbit` |

## Correlation és logolás (U: `Infrastructure/BehaviorTests`, `Infrastructure/LoopbackAndRoutingTests`)

- Az `ICorrelation` property-k: `CorrelationId`, `CausationId`, `CurrentId`.
- A `LogContext` property-k: `correlation-id`, `causation-id`, `command-id`, `query-id`, `event-id`.
- A strukturált property-k: `correlation`, `commandData`, `queryData`, `eventData`, `queryResult`.

## Metrikák és health (U: `Infrastructure/MonitoringTests`, `AspNet/HostingTests`)

- A metrikák nevei, help szövegei, típusai, label-nevei és label-sorrendje, valamint az időtartamok rögzítésének szabályai a [Megfigyelhetőség](../observability.md#metrikák-prometheus) oldalon találhatók.
- A metrika endpoint a `GET /metrics`.
- A health endpointok a `/.well-known/ready` és a `/.well-known/live`.
- A tagek a `Readiness` és a `Liveness`, a beépített check neve `self`.

## Konfiguráció (U: `Rabbit/RabbitConnectionOptionsTests`, `Rabbit/RabbitTopologyTests`)

- A section neve `ServiceComponents:RabbitConnectionOptions`.
- A property-nevek és az alapértékek a [Konfiguráció](../configuration.md) oldalon találhatók.

## Mediator szemantika (U: `Infrastructure/MediatorPipelineTests`)

- A hookok sorrendje az [Üzenetkezelési fogalmak](../concepts/messaging.md#hookok) oldalon található.
- Az event handlerek párhuzamosan futnak, egy gyerek scope-ban.
- A hiányzó handlerek hibaüzeneteinek szövege nem változik.
