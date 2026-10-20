**Magyar** | [English](../../en/migration/0.2-to-0.3.md)

# Átállás 0.2-ről 0.3-ra

A 0.3 a belső működés újraírása. A publikus API (forráskompatibilitás) és a [wire protocol](../reference/wire-protocol.md) változatlan marad, ezért:

- a csomagok frissítése nem igényel kódmódosítást;
- a 0.2-es és a 0.3-as szolgáltatások HTTP-n és RabbitMQ-n is együtt tudnak működni.

## Csomagcsere

A 0.3 új csomag ID-kkel jelenik meg; a benne lévő assembly-k neve és namespace-e változatlan.

| Eredeti csomag (0.2) | Csomag (0.3) |
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

- A `PackageReference`-ekben (vagy a `Directory.Packages.props`-ban) kell lecserélni az ID-ket, kódot nem kell módosítani. A tranzitívan érkező csomagok (például a `Core` az `AspNet`-en keresztül) a szülőjükkel együtt cserélődnek.
- Egy projekt nem hivatkozhat egyszerre eredeti és új csomagra, tranzitívan, egy másik könyvtáron keresztül sem: mindkettőben ott van például a `ServiceComponents.Core.dll`. A közös könyvtárakat az őket használó szolgáltatásokkal együtt kell átállítani.
- Támogatott kiinduló verziók: `0.2.0-beta.12` és a korábbi `0.2.1-beta.1` (ez egy régebbi commitból készült, hiányzik belőle a `ProcessContextBehavior`).
- Az eredeti és az új csomagokat futtató szolgáltatások HTTP-n és RabbitMQ-n együttműködnek, így a szolgáltatások egyenként állíthatók át.

## Kompatibilis javítások

Az [ismert hibák](../known-issues.md) javításai, amelyek nem érintik a 0.2-t futtató szolgáltatásokat.

### ServiceComponents.Core

| Változás | Ismert hiba | Teszt |
|---|---|---|
| A `ValueObject.Equals` a teljes értéksorozatot hasonlítja. Az eltérő számú értéket tartalmazó objektumok egyik irányban sem egyenlők. | #9 | `ValueObjectTests.Different_value_counts_are_not_equal` |
| A `ValueObject.GetHashCode` üres értéklistánál nem dob kivételt, és sorrendhelyesen hash-el. | #9 | `ValueObjectTests.Empty_value_object_has_a_hash_code` |
| A `TimeIntervalExtensions.Contains(DateTime)` a félig nyitott [Start, Finish) intervallum belső pontjaira ad igazat. Korábban az intervallum előtti pontokra adott igazat. | #10 | `TimeIntervalTests.Contains_datetime_of_half_open_interval` |
| A `TimeIntervalExtensions.DurationRateOf` a valódi arányt adja vissza egész osztás helyett. | #10 | `TimeIntervalTests.DurationRateOf_is_a_fraction` |
| A `TimeIntervalExtensions.Simplify` mindig összevonja az érintkező intervallumokat, és kezdés szerint rendezve adja vissza őket. | #10 | `TimeIntervalTests.Simplify_merges_touching_intervals_regardless_of_order` |
| Az `Enumeration.GetAll` figyelmen kívül hagyja a más típusú statikus mezőket, és nem száll el miattuk. | kisebb | `EnumerationTests.Other_static_fields_are_ignored` |
| A `NotFoundException(string, Exception)` is 404-es hibakódot állít be, mint a többi konstruktor. | kisebb | `CoreMiscTests.Derived_business_exceptions` |
| A `StringHelper.Random` a `RandomNumberGenerator.GetString`-et használja (egyenletes eloszlás, modulo-torzítás nélkül). | | |

### ServiceComponents.Domain

| Változás | Ismert hiba | Teszt |
|---|---|---|
| A hiányzó `Apply` metódus kiderül, mielőtt a verzió nő vagy az event rögzül. | #15 | `EventSourcedAggregateRootTests.Missing_apply_method_throws_without_changing_the_aggregate` |
| Az ősosztályok nem publikus `Apply` metódusait is megtalálja. | #15 | `EventSourcedAggregateRootTests.Private_apply_methods_of_base_classes_are_found` |
| Az `Apply` metódus keresésének eredménye aggregate- és eventtípusonként cache-elődik. | | |

Ezek változatlanok maradnak:

- a verzió az `Apply` futása előtt nő;
- az `Apply` kivételei `TargetInvocationException`-be csomagolva jönnek.

### ServiceComponents.Infrastructure

| Változás | Ismert hiba | Teszt |
|---|---|---|
| A dispatcherek, a hookok és a validáció `dynamic` helyett cache-elt, típusos invokereken keresztül hívják a handlereket. A nem publikus handlerek és hookok is működnek. | #21 | `MediatorPipelineTests.Non_public_handler_is_dispatched` |
| Az event handlerek megkapják a dispatcholó scope correlationjét (`CorrelationId`, `CausationId`, `CurrentId`), az általuk küldött eventek folytatják a láncot. | #1 | `LoopbackAndRoutingTests`, `RabbitConsumerTests.Event_is_dispatched_and_acked`, `HttpReceiverContractTests.Event_is_received` |
| Az `AddMediator` egyszer regisztrál egy osztályt akkor is, ha több handler-interfészt implementál (korábban minden eventet háromszor kezelt). | #20 | `MediatorPipelineTests.Class_with_several_handler_interfaces_is_registered_once` |
| Request constraint. A csökkentés helyes: nullánál a kulcs törlődik. Többszöri befejezés nem dob kivételt. Ha egy későbbi kulcs elutasít, a korábbiak visszagörgetődnek. Egy beágyazott kérés elutasítása miatt nem szivárog el a külső kérés helye. A takarítás nem függ a megszakított tokentől, és nem fedi el az eredeti kivételt. | #5 | `BehaviorTests.Constraint_*` |
| Prometheus. A reflexió eredménye metrikaosztályonként cache-elődik. Attribútum nélküli osztálynál egyértelmű `InvalidOperationException` jön. A null label-értékekből üres string lesz. Az `Observe(value, count)` nem bukik el property-vel rendelkező osztálynál, és exponenciális hisztogramnál exponenciális bucketeket használ. | #11 | `MonitoringTests` |
| Event recorder. Nincs elveszett ébresztés a keresés és a regisztráció között. A megszakított várakozók törlődnek. A bázistípusra várók élő eventeket is kapnak. Timeoutnál továbbra is `default` a visszatérési érték. | #12 | `EventRecorderTests` |
| Loopback. Regisztrált `ICorrelation` nélkül is működik, ilyenkor új correlation lánc indul. A `RoutableEvent` kicsomagolva, a belső event handlereihez jut el. | kisebb | `LoopbackAndRoutingTests`, `KnownDefectsTests.Routable_event_is_unwrapped_by_loopback` |
| `RequestParser`, beágyazott `$type` esetén csak ezek a típusok engedélyezettek: üzenettípusok, az `AddMediator`-nak vagy a `RequestParser.AllowTypesFrom`-nak átadott assembly-k típusai, egy üzenet assembly-jében deklarált típusok, valamint egyszerű .NET érték- és gyűjteménytípusok. Minden mást `InvalidOperationException`-nel (HTTP 400) utasít el. Típusnév megadásakor a nem command/query/event típusokat nem példányosítja. A típusnevek feloldása cache-elődik. | biztonság | `RequestParserTests` |

Új API (additív): `RequestParser.AllowTypesFrom(params Assembly[])`.

### ServiceComponents.Infrastructure.Rabbit és AddRabbit

| Változás | Ismert hiba | Teszt |
|---|---|---|
| A kapcsolat megtartja az URI-ban (`amqps://host/vhost`) megadott virtual hostot és TLS beállítást. A `VirtualHost` és az `Ssl` csak akkor érvényesül, ha nem az alapértékre van állítva. | #2 | `KnownDefectsTests.Rabbit_connection_factory_keeps_vhost_and_tls_from_uri` |
| A query válaszát aszinkron várja, nem blokkol thread-pool szálat. A válaszqueue hiba és megszakítás esetén is törlődik. Az átbocsátás nem változott (lásd [Teljesítmény](../performance.md)). | #3 | `RabbitSenderTests.Query_without_reply_waits_until_cancelled_and_cleans_up` |
| Ha a handler `null`-t ad vissza, a query `null` választ kap. Korábban a hívó örökké várt. A 0.2-es senderek is `null`-ként olvassák ezt a választ. | #3 | `RabbitConsumerTests.Null_query_result_is_answered_with_null` |
| Ha az ack után a query válaszának küldése hibázik, a hiba csak naplózódik, az üzenet nem kap még egy rejectet. | #7 | `KnownDefectsTests.Rabbit_consumer_does_not_reject_after_ack_when_reply_fails` |
| Az `AddRabbit` a beállított routing key-jel küldi a commandokat, azzal, amellyel a queue kötve van. | #8 | `RabbitWireupTests.AddRabbit_commands_and_retry_bindings_use_the_routing_key`, integrációs `Commands_arrive_with_a_non_empty_routing_key` |
| A retry lánc a beállított routing key-jel is kötve van. A kulcsot megtartó dead-letterelt üzenetek így újrapróbálásra kerülnek, nem vesznek el. A kötések additívak, ezért a 0.2-es szolgáltatások ugyanezt a topológiát deklarálhatják. Új overload: `AddRabbitRetry(channel, scope, queue, ttls, routingKey)`. | #8 | integrációs `Routed_events_survive_the_retry_chain` |
| Az `AddRabbit(Func…)` elfogadja, ha a routing key és a retry intervallum builder nincs megadva. | #6 | `RabbitWireupTests.AddRabbit_configuration_overload_without_optional_builders` |
| Az `AddRabbitEventPublisher` figyelembe veszi a `mandatory` paramétert, és a `basicProperties`-t minden üzenet sablonjaként használja. | kisebb | `RabbitSenderTests.Event_publisher_uses_mandatory_and_basic_properties_template` |
| A consumer `dynamic` helyett típusos invokerrel fogadja a queryket. | | |

Nem változott:

- a queryk továbbra is az exchange nevét viselő queue-ba mennek;
- a beállítottól eltérő routing key-jű `RoutableEvent`-ek retryje továbbra sem működik.

### ServiceComponents.AspNet

| Változás | Ismert hiba | Teszt |
|---|---|---|
| Az `AddHttpReceiverAuthorizationBehavior` a HTTP receiver láncot dekorálja, így a HTTP correlation behavior továbbra is lefut. A handler osztályát a regisztrációból olvassa ki, nem oldja fel a handlert; a lambdás regisztrációkat továbbra is feloldja. A behavior-ök publikus konstruktorai változatlanok. | #4 | `HostingTests.Authorized_caller_keeps_http_correlation`, `AuthorizationBehaviorTests` |
| A `JsonExceptionMapper` az üres, a nem JSON és a típus nélküli hiba-body-kat a státuszkód alapján képezi le (401, 404, 501, 400), nem dob `JsonReaderException`-t vagy `ArgumentNullException`-t. A választ a leképezés előtt buffereli, így az alapértelmezett mapper nem blokkol rajta. | #13 | `HttpSenderContractTests.Non_json_or_untyped_error_bodies_map_by_status_code`, `KnownDefectsTests.Exception_mapper_maps_empty_401_body` |
| Az `ErrorHandlingMiddleware` az 5xx-re leképzett hibákat `Error`, a többit `Debug` szinten logolja. Ha a válasz már elindult, logol és továbbdobja a kivételt, ahelyett hogy az írásnál hibára futna. A státuszkódok és a body-k változatlanok. | #14 | `KnownDefectsTests.Error_middleware_*` |
| A `ServiceComponentsHostBuilder` a container felépítése után, az application builder callbackek előtt meghívja a `StartupCallbacks`-et (`RegisterCallback(Action<IConfiguration, ILifetimeScope>)`) a root scope-pal. | #19 | `HostingTests.Startup_callbacks_are_invoked_with_the_root_scope` |
| A HTTP sender kérésenként állítja be az `Accept` headert, így az közös `HttpClient` esetén sem halmozódik. Az érték nélküli headereket kihagyja (ezek a drótra sem kerültek ki). | kisebb | `HttpSenderContractTests.Reused_client_does_not_accumulate_accept_header` |
| A HTTP event publisher a `RoutableEvent` belső eventjét küldi el. | kisebb | `HttpSenderContractTests.Routable_event_is_posted_as_the_wrapped_event` |
| A `RequestBinder` nem request típusoknál `Failed()` eredményt hagy. | kisebb | `HostingTests.Request_binder_fails_for_non_request_types` |
| A `RabbitMQConsumerChecker` elfogadja a duplikált és a hiányzó consumer tageket; egy unhealthy consumert nem takar el egy azonos tagű healthy. | kisebb | `KnownDefectsTests.Consumer_health_check_tolerates_duplicate_and_missing_tags` |

> **Ellenőrizendő viselkedés:** a 0.2-ben regisztrált `StartupCallbacks` bejegyzések soha nem futottak le; a 0.3-ban induláskor lefutnak.

### Integrációk

| Változás | Ismert hiba | Teszt |
|---|---|---|
| Az `NhibernateModule` a session helyett a session factoryt aktiválja automatikusan: nem marad nyitva root `ISession`, a hibás connection string viszont továbbra is induláskor kiderül. | kisebb | `IntegrationPackageTests.NHibernate_module_builds_the_session_factory_on_container_build` |
| Az `NHibernateEventPublisherBehavior` megvalósítja az `IAsyncDisposable`-t, és minden eventet egyszer ad tovább, ismételt dispose esetén is. | kisebb | `IntegrationPackageTests.NHibernate_event_publisher_*` |
| A `CouchDbConfigurationSource` nem sikeres válasz esetén `InvalidOperationException`-t dob, ahelyett hogy a hibadokumentumot töltené be. | kisebb | `IntegrationPackageTests.CouchDb_configuration_source_rejects_error_responses` |
| A `ServiceComponents.Host` tartalmazza a státusz badge-eket (`AddBadges`, `AddBadgeService`, `MapBadges`, `BadgeOptions.ConfigurationSectionName`). A 0.2-es csomagban nem volt lefordított kód, így ez additív. A metrics controller és a request binder forrásai a `ServiceComponents.AspNet` duplikátumai voltak, ezért törlődtek. A badge-ek rajzolása System.Drawing nélkül történik, így a csomag már nem függ a DotBadge-től és a sérülékeny `System.Drawing.Common` 4.6.0-tól, és Linuxon is működik. | | `BadgeTests` |

Új API (additív): `NHibernateEventPublisherBehavior.DisposeAsync()` és a fenti `ServiceComponents.Host` API.

### ServiceComponents.Testing

| Változás | Ismert hiba | Teszt |
|---|---|---|
| Az output helper nélküli `TestHost<T>` (például amit a `UseTestSender` hoz létre) a teszt-kimeneti sink nélkül indul el, ahelyett hogy kivételt dobna. A `UseOutputHelper(null)` továbbra is kivételt dob. | kisebb | `samples/Orders.Tests/TestSenderTests` |
| A `UseTestSender` a `/api/generic` útvonalra küld (korábban a host gyökerére küldött), és a test hostot a containerrel együtt zárja le. A `UseTestSenderAt(requestUri, key)` más útvonalat választ. | kisebb | `samples/Orders.Tests/TestSenderTests` |
| A `UseOutputHelper` Seq sinkje a `ServiceComponents:Testing:SeqServerUrl` kulccsal átirányítható vagy kikapcsolható; az alapértelmezés továbbra is `http://localhost:5341`. | kisebb | `TestingPackageTests.UseOutputHelper_writes_to_the_output_helper_with_configurable_seq` |

Új API (additív): `ContainerBuilderExtensions.UseTestSenderAt<TTestHost>(requestUri, key)`.



## Opt-in változások

Ezek a változások a drótot is érintik, ezért alapból ki vannak kapcsolva. Csak akkor kapcsold be őket, ha minden partner 0.3-at futtat.

`RabbitMessagingOptions` (section: `ServiceComponents:RabbitMessagingOptions`, vagy regisztrált példány):

| Beállítás | Alapérték | Hatás |
|---|---|---|
| `ReplyOnQueryFailure` | `false` | Ha a query handler hibázik, a consumer hibaválaszt is küld (`x-sc-error-kind`, `x-sc-error-message`, `x-sc-error-code` headerek). A 0.3-as sender ilyenkor nem vár, hanem a megfelelő kivételt dobja. **A 0.2-es senderek a hibaválaszt `null` eredménynek olvasnák**, ezért csak akkor kapcsold be, ha minden sender 0.3. |
| `QueryTimeoutInSeconds` | nincs | A sender ennyi idő után `TimeoutException`-nel áll le. Csak helyi hatású, ezért 0.2-es partnerek mellett is biztonságos. |
| `PrefetchCount` | `0` | Consumerenkénti `basic.qos` korlát. Csak helyi hatású, ezért 0.2-es partnerek mellett is biztonságos. |

## Csomagfüggőségek

- A `ServiceComponents.Infrastructure` már nem függ a `System.ComponentModel.Annotations` csomagtól, mert a .NET 10 tartalmazza ezt az assembly-t.
- A többi csomagfüggőség és azok verziója változatlan. A verziókat mostantól központilag a `src/Directory.Packages.props` kezeli.
- A csomagok mostantól XML dokumentációs fájlt is tartalmaznak.

## Eltávolítva

Az alábbi üres projekteket sosem publikáltuk, ezért törlésre kerülnek:

- `ServiceComponents.Application.HandlerContext`
- `ServiceComponents.Application.Http`
- `ServiceComponents.Application.Mediator`
- `ServiceComponents.Infrastructure.HandlerContext`
- `ServiceComponents.Infrastructure.Http`
- `ServiceComponents.Infrastructure.Mediator`
- `ServiceComponents.Infrastructure.Validation`
