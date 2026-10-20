**Magyar** | [English](../en/known-issues.md)

# Ismert hibák

Ez az oldal a jelenlegi verzió hibáit sorolja fel. Mindegyiket egy karakterizációs teszt rögzíti (`// BUG:` megjegyzés vagy `*_current_behavior*` név).

A közelgő újraírás így kezeli őket:

- **kompatibilis javítás:** nem érinti a régi verziót futtató szolgáltatásokat, ezért azonnal bekerül;
- **opt-in:** megváltoztatja a drótot, ezért csak kapcsolóval aktiválható.

A javítással együtt a teszt is megváltozik, és a változás bekerül a migrációs jegyzetbe.

## Súlyos

| # | Hiba | Hol | Tervezett javítás |
|---|---|---|---|
| 1 | ✅ **Javítva a 0.3-ban.** **Az event handlerek elveszítik a correlationt.** Az `EventDispatcher` gyerek scope-ból oldja fel a handlereket, ahol új, üres `Correlation` van. Ezért az event handlerekből küldött üzenetek új correlation láncot kezdenek. | `EventDispatcher` | kompatibilis |
| 2 | ✅ **Javítva a 0.3-ban.** **A RabbitMQ URI-ból jövő vhostot és TLS-t felülírja a rendszer.** A `VirtualHost` (`/`) és az `Ssl` (`false`) beállítás az URI után érvényesül, így az `amqps://host/vhost` a `/` vhostra csatlakozik, TLS nélkül. | `RabbitAutofacExtensions.CreateConnectionFactory` | kompatibilis: csak akkor érvényesül, ha be van állítva |
| 3 | ✅ **Javítva a 0.3-ban** (null eredmény, takarítás, timeout és hibaválasz opció). **A RabbitMQ-n küldött query örökre várhat.** A query az exchange nevét viselő queue-ba megy. Nincs timeout. Null eredményre és handlerhibára nem megy válasz. A válaszqueue megmarad. | `RabbitQuerySender`, `RabbitConsumer` | timeout opció (kompatibilis); hibaválasz opt-in |
| 4 | ✅ **Javítva a 0.3-ban.** **Az authorizációs decorator kikerüli a HTTP correlationt.** A dekorált HTTP receiver helyett az `IReceiveCommand`/`IReceiveQuery`-nek adja tovább a kérést. | `HttpCommandAuthorizationBehavior`, `HttpQueryAuthorizationBehavior` | kompatibilis |
| 5 | ✅ **Javítva a 0.3-ban.** **Hibás a request constraint számláló.** A csökkentés fordított: nem nulla értéknél törli a kulcsot, nullánál beírja. A második befejezés `ArgumentNullException`-t dob. Ha egy későbbi kulcs elutasít, a korábban megnövelt kulcsok nem csökkennek vissza. | `RequestExecutionConstraintBehavior` | kompatibilis |

## Közepes

| # | Hiba | Hol |
|---|---|---|
| 6 | ✅ **Javítva a 0.3-ban.** Az `AddRabbit(Func…)` `NullReferenceException`-t dob, ha az opcionális routing key és retry builder nincs megadva. | `ServiceComponentsRabbitHostBuilderExtensions` |
| 7 | ✅ **Javítva a 0.3-ban.** Ha a query válasz küldése az ack után hibázik, az üzenet rejectet is kap. Egy valódi broker ilyenkor lezárja a channelt (406). | `RabbitConsumer` |
| 8 | ✅ **Javítva a 0.3-ban.** Elvesznek a routing key-jel küldött üzenetek. Nem üres `routingKey` esetén az `AddRabbit` commandjai soha nem érkeznek meg (üres kulccsal mennek ki), a routing key-jel küldött üzenetek pedig az első retrynél elvesznek (a retry kötések "" kulcsot használnak). | `AddRabbit`, `AddRabbitRetry` |
| 9 | ✅ **Javítva a 0.3-ban.** A `ValueObject.Equals` eltérő számú értéknél aszimmetrikus. Üres értéklistánál a `GetHashCode` kivételt dob. | `ValueObject` |
| 10 | ✅ **Javítva a 0.3-ban.** A `TimeInterval.Contains(DateTime)` fordítva hasonlít. A `DurationRateOf` egész osztást végez. A `Simplify` eredménye érintkező intervallumoknál a bemenet sorrendjétől függ. | `TimeIntervalExtensions` |
| 11 | ✅ **Javítva a 0.3-ban.** Prometheus: az exponenciális hisztogram lineáris bucketeket kap. Az `Observe(val, count)` labeleket használ olyan hisztogramon, amely label-nevek nélkül jött létre. Attribútum vagy névtér nélküli típusoknál `NullReferenceException` keletkezik. | `PrometheusMetricsService`, `MetricExtensionMethods` |
| 12 | ✅ **Javítva a 0.3-ban.** Az `EventRecorderService.WaitFor` timeoutnál `null`-t ad vissza. Megszakított tokennél kivételt dob. A bázistípusra várakozó csak a már rögzített eventeket találja meg. | `EventRecorderService` |
| 13 | ✅ **Javítva a 0.3-ban.** A `JsonExceptionMapper` üres vagy nem JSON hibatestnél (például JWT 401 esetén) `JsonReaderException`-t, hiányzó `type` mezőnél `ArgumentNullException`-t dob. | `JsonExceptionMapper` |
| 14 | ✅ **Javítva a 0.3-ban.** Az `ErrorHandlingMiddleware` nem logol. | `ErrorHandlingMiddleware` |
| 15 | ✅ **Javítva a 0.3-ban.** Az `EventSourcedAggregateRoot` már azelőtt növeli a verziót és eltárolja az eventet, hogy ellenőrizné, van-e `Apply` metódus. Az ősosztály privát `Apply` metódusait nem találja meg. | `EventSourcedAggregateRoot` |
| 16 | Ha egy üzenetnek nincs `commandId` / `queryId` / `eventId` konstruktorparamétere, a fogadó oldalon új azonosítót kap. | üzenetkontraktusok |
| 17 | Az `IPostHandleQuery<T,R>.PostHandleAsync` eredményét a rendszer figyelmen kívül hagyja. | `QueryDispatchBehavior` |
| 18 | Az event handlerek egy közös scope-ban, párhuzamosan futnak, így nem szálbiztos szolgáltatásokon (például `ISession`) osztoznak. | `EventDispatcher` |
| 19 | ✅ **Javítva a 0.3-ban.** A `StartupCallbacks`-et a host builder összegyűjti, de soha nem hívja meg. | `ServiceComponentsHostBuilder` |
| 20 | ✅ **Javítva a 0.3-ban.** A több handler-interfészt implementáló osztály scanenként egyszer regisztrálódik, ezért minden eventet háromszor kezel. | `AddMediator` |
| 21 | ✅ **Javítva a 0.3-ban.** A handlereknek publikusnak kell lenniük: a dispatch `dynamic`-ot használ, ami nem publikus típusnál `RuntimeBinderException`-t dob. | dispatcherek, hookok |

## Kisebb

- ✅ **Javítva a 0.3-ban. RabbitMQ event publisher:** az `AddRabbitEventPublisher` figyelmen kívül hagyta a `mandatory` és a `basicProperties` paramétert.
- ✅ **Javítva a 0.3-ban. HTTP sender:** a `HttpSender` minden hívásnál hozzáfűzi az `accept: application/json` headert a kliens alapértelmezett headereihez.
- ✅ **Javítva a 0.3-ban. Request binder:** a `RequestBinder` nem request típusoknál a `Failed()` eredményt felülírja `Success`-szel.
- **Háttérsor:** a `BackgroundTaskQueue` megtelt állapotban a legrégebbi elemet eldobja.
- ✅ **Javítva a 0.3-ban (root session). NHibernate session:**
  - az `NhibernateModule` automatikusan aktivál egy root `ISession`-t, amely az alkalmazás teljes élettartamáig nyitva marad;
  - a container felépítéséhez elérhető adatbázis kell.
- ✅ **Javítva a 0.3-ban (async dispose; a regisztráció továbbra is kézi). NHibernate event publisher:** az `NHibernateEventPublisherBehavior` a `Dispose`-ban, blokkoló `Wait()`-tel publikál, és sehol nincs regisztrálva.
- ✅ **Javítva a 0.3-ban. Testing csomag:**
  - a `UseTestSender` nem működik, mert az új test hostnak nincs output helpere;
  - a `UseOutputHelper` mindig a `localhost:5341` címen futó Seq-be is ír.
- ✅ **Javítva a 0.3-ban. `RoutableEvent`:** csak a RabbitMQ publisher bontja ki; loopbacken és HTTP-n a belső event nem jut el a handleréhez.
- ✅ **Javítva a 0.3-ban. CouchDB:** a `CouchDbConfigurationSource` nem ellenőrzi a HTTP státuszkódot.
- ✅ **Javítva a 0.3-ban. Health check:** a `RabbitMQConsumerChecker` duplikált consumer tag esetén kivételt dob.
- ✅ **Javítva a 0.3-ban. Biztonság:** a típusnév nélküli üzeneteket a rendszer `TypeNameHandling.All`-lal deserializálja, így a hívó tetszőleges típust példányosíthat. Az újraírás üzenettípus-allowlistet vezet be.
- ✅ **Javítva a 0.3-ban. `Enumeration`:** az `Enumeration.GetAll` elbukott, ha az osztálynak más statikus mezője is van.
- **Post hookok:** a sorrendjük eltér a commandoknál és a queryknél/eventeknél.
- ✅ **Javítva a 0.3-ban. `NotFoundException`:** a `NotFoundException(string, Exception)` hibakódja 0 volt, nem 404.
