**Magyar** | [English](../../en/concepts/messaging.md)

# Üzenetkezelési fogalmak

## Üzenetek

| Fajta | Ősosztály | Azonosító | Ki kezeli | Eredmény |
|---|---|---|---|---|
| Command | `Command` (`ICommand`) | `CommandId` | pontosan egy `IHandleCommand<T>` | nincs |
| Query | `Query<TResult>` (`IQuery<TResult>`) | `QueryId` | pontosan egy `IHandleQuery<T, TResult>` | `TResult` |
| Event | `Event` (`IEvent`) | `EventId` | nulla vagy több `IHandleEvent<T>` | nincs |

- **Azonosítók.** Ha nem adsz meg azonosítót, új GUID string keletkezik.
- **`RoutableEvent(event, routingKey)`.** Becsomagol egy eventet, hogy megadott routing key-jel menjen RabbitMQ-ra. Csak a RabbitMQ publisher bontja ki.

## Dispatch

Az `AddMediator(assemblies)` a megadott assembly-kben megkeresi az `IHandleCommand<>`, `IHandleQuery<,>` és `IHandleEvent<>` zárt implementációit, és függőségenként (per dependency) regisztrálja őket. A handlert az üzenet **futásidejű típusa** választja ki.

| Helyzet | Viselkedés |
|---|---|
| Nincs command handler | `InvalidOperationException("No handler registered for <Type>")` |
| Nincs query handler | `InvalidOperationException("No handler found for <Type>")` |
| Nincs event handler | Nem történik semmi |
| Több event handler | **Párhuzamosan** futnak (`Task.WhenAll`), egy gyerek lifetime scope-ban |

## Hookok

Az `AddMediatorBehavior(assemblies)` a dispatchereket az assembly-kben talált pre, post és failure hookokkal veszi körül.

Minden hooknak két változata van:

- a **nem generikus** minden üzenetet lát az adott fajtából;
- a **generikus** csak egy üzenettípust.

| Hook | Nem generikus | Generikus |
|---|---|---|
| A handler előtt | `IPreHandleCommand` / `Query` / `Event` | `IPreHandleCommand<T>`, `IPreHandleQuery<T,R>`, `IPreHandleEvent<T>` |
| A handler után | `IPostHandleCommand` / `Query` / `Event` | `IPostHandleCommand<T>`, `IPostHandleQuery<T,R>`, `IPostHandleEvent<T>` |
| Kivétel esetén | `IHandleCommandFailure` / `Query` / `Event` | `IHandleCommandFailure<T>`, `IHandleQueryFailure<T,R>`, `IHandleEventFailure<T>` |

**Futási sorrend** (a post sorrend fajtánként eltér):

| Fajta | Sorrend |
|---|---|
| Command | pre → pre&lt;T&gt; → handler → post&lt;T&gt; → post |
| Query, event | pre → pre&lt;T&gt; → handler → post → post&lt;T&gt; |
| Kivétel esetén | failure → failure&lt;T&gt;, majd a kivétel újradobódik |

Az `IPostHandleQuery<T,R>.PostHandleAsync` visszatérési értékét a rendszer figyelmen kívül hagyja, a hívó az eredeti eredményt kapja.

## Beépített behavior-ök

| Regisztráció | Hatás |
|---|---|
| `AddValidationBehavior(apiAssemblies)` | Regisztrálja a FluentValidation validátorokat, és generikus pre hookként futtatja őket. Hiba esetén `ValidationException`-t dob. Az `AddMediatorBehavior` is kell hozzá. |
| `AddLogBehavior()` | Logolja a feldolgozást, a sikert (verbose, eventeknél debug) és a hibát (error) |
| `AddStopwatchBehavior()` | Kérésenként logolja az időtartamot (verbose) |
| `AddCorrelationInfo()` | Regisztrálja a scope-onkénti `Correlation` / `ICorrelation` példányt |
| `AddReceiverCorrelationLogEnricherBehavior()` | A correlation azonosítókat a Serilog `LogContext`-be teszi |
| `AddPrometheusRequestMetricsService()` + `AddPrometheusRequestMetricsBehavior()` | Kérésmetrikák (lásd [Megfigyelhetőség](../observability.md)) |
| `AddRequestConstraints(keys, constraint, expiry)` | Kulcsonként korlátozza a párhuzamos futást `IDistributedCache` segítségével. Hiba esetén `CommandConstraintException`-t dob (lásd [Ismert hibák](../known-issues.md)). |
| `UseEventRecorder()` | Rögzíti a kezelt eventeket; az `IEventRecorder.WaitFor` meg tud várni egyet (tesztekhez hasznos) |

A `UseDefault` regisztrálja a mediatort, a hookokat, a validációt, a logolást, a stopwatchot, az összes correlation behavior-t, a receivereket és a loopback sendert.

## Küldés és routing

A handlerek `ISendCommand`, `ISendQuery` és `IPublishEvent` függőséget kérnek. Ha routerek is regisztrálva vannak, ezek a `CommandRouter`, `QueryRouter` és `EventRouter` példányok lesznek. A router meghív egy kulcsválasztó függvényt, és az adott kulccsal regisztrált sendert használja:

```csharp
.AddCommandRouter(command => "loopback")
.AddQueryRouter(query => query is GetOrder ? "http" : "loopback")
.AddEventRouter(@event => "rabbit")
```

| Kulcs | Sender | Ki regisztrálja |
|---|---|---|
| `loopback` | Folyamaton belüli: ugyanannak a szolgáltatásnak egy új lifetime scope-jában dispatchel | `UseDefault` / `AddLoopbackSender("loopback")` |
| `rabbit` | RabbitMQ command sender, query sender (RPC), event publisher | `AddRabbit` |
| saját kulcs | HTTP command sender, query sender, event publisher | `AddHttpSender(uri, key)` |

Az `EventRouter.PublishAsync(IEnumerable<IEvent>)` egyenként publikálja az eventeket.

## Correlation

Minden kérés-scope-nak egy `Correlation` példánya van:

| Mező | Jelentés |
|---|---|
| `CorrelationId` | Ugyanaz minden üzenetnél, amelyet ugyanaz az eredeti kérés váltott ki |
| `CausationId` | Annak az üzenetnek az azonosítója, amely ezt kiváltotta |
| `CurrentId` | Az éppen kezelt üzenet azonosítója |

A fogadó scope correlationjét a transport tölti ki:

| Transport | `CorrelationId` | `CausationId` | `CurrentId` |
|---|---|---|---|
| HTTP | `correlation-id` header, vagy új GUID | `causation-id` header | az üzenet azonosítója |
| RabbitMQ | `CorrelationId` property, vagy új GUID | `causation_id` header | az üzenet azonosítója |
| Loopback | a szülő `CorrelationId`-ja, vagy új GUID | a szülő `CurrentId`-ja | az üzenet azonosítója |

A senderek az aktuális scope `CorrelationId`-ját és `CurrentId`-ját (causationként) teszik ki a drótra.

> Az event handlerek egy gyerek scope-ból oldódnak fel, amely örökli ezt a correlationt (0.3-tól; a 0.2-ben üres correlationt láttak).
