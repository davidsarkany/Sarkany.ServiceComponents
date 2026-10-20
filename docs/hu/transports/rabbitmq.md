**Magyar** | [English](../../en/transports/rabbitmq.md)

# RabbitMQ transport

## Gyors beállítás: `AddRabbit`

```csharp
hostBuilder.AddRabbit(
    connectionString: "amqp://guest:guest@localhost:5672",
    clientName: "orders",
    queue: "orders",
    exchange: "orders-events",
    routingKey: "",
    retryIntervals: new[] { 1000, 5000 });   // opcionális, ezredmásodpercben
```

Az `AddRabbit` a következőket regisztrálja.

**Kapcsolatok és channelek**

| Cél | Kliensnév | Kapcsolat | Channel |
|---|---|---|---|
| Topológia deklarálása | `{clientName}-generic` | alapértelmezett | alapértelmezett |
| Senderek | `{clientName}-publisher` | `publisher` kulcs | `publisher` kulcs |
| Consumerek | `{clientName}-consumer` | `consumer` kulcs | consumerenként egy |

**Consumerek**

- Darabszámuk `Environment.ProcessorCount - retryIntervals.Length`, legalább 1.
- Minden `i` consumer saját channelt kap, a consumer tagje `{clientName}-consumer-{i}`.
- `retryIntervals` megadása esetén intervallumonként még egy consumer olvassa a `{queue}-retry-{ttl}` queue-t, `{clientName}-consumer-retry-{ttl}` taggel.

**Senderek és receiverek**

- `rabbit` kulccsal: event publisher (exchange és routing key), command sender (exchange és routing key; a 0.2 üres kulcsot használt) és query sender.
- Ezen felül a receiverek és egy induláskor futó komponens.

**Indulás**

A container felépítésekor az induló komponens három dolgot csinál:

1. deklarálja az exchange-et, a queue-t és a kötésüket;
2. ha meg van adva `retryIntervals`, deklarálja a retry topológiát is;
3. elindítja az összes consumert.

**A brokernek ekkor már elérhetőnek kell lennie.**

### Buktatók

- **`Func<IConfiguration, …>` overload (csak 0.2).** A 0.2-ben akkor is meghívta az opcionális routing key és retry buildert, ha null volt, és `NullReferenceException`-t dobott; 0.2 alatt add meg mind a hat builder függvényt. A 0.3-ban javítva.
- **Routing key (csak 0.2).** A 0.2-ben nem üres routing key esetén a queue ezzel a kulccsal kötődik, az `AddRabbit` command sendere viszont üres kulccsal publikál, így a commandok nem érkeznek meg. A 0.3-ban javítva.
- **Ha RabbitMQ-n küldesz queryt, nevezd el a queue-t ugyanúgy, mint az exchange-et.** A query RPC a default exchange-re publikál, és az *exchange nevét* használja routing key-ként. Csak az exchange nevét viselő queue kapja meg a queryt. Ha nincs ilyen, a query elveszik, a hívó pedig addig vár, amíg a tokenje meg nem szakad.

## Topológia

Minden objektum **non-durable és auto-delete**, az exchange-ek `direct` típusúak.

| Objektum | Név | Argumentumok / kötés |
|---|---|---|
| Exchange | `{exchange}` | direct |
| Queue | `{queue}` | argumentum nélkül; retry esetén `x-dead-letter-exchange={queue}-dlx-wait-{ttl0}` |
| Kötés | `{queue}` ← `{exchange}` | `routingKey` routing key-jel |

`retryIntervals = [t0, t1, …]` esetén minden `ttl`-re:

| Objektum | Név | Argumentumok |
|---|---|---|
| Exchange | `{queue}-dlx-wait-{ttl}` | |
| Exchange | `{queue}-dlx-retry-{ttl}` | |
| Queue | `{queue}-wait-{ttl}` | `x-message-ttl={ttl}` (int), `x-dead-letter-exchange={queue}-dlx-retry-{ttl}`, a `{queue}-dlx-wait-{ttl}` exchange-hez kötve "" kulccsal |
| Queue | `{queue}-retry-{ttl}` | `x-dead-letter-exchange` = a következő `{queue}-dlx-wait-…`, az utolsónál `{queue}-dlx`; a `{queue}-dlx-retry-{ttl}` exchange-hez kötve "" kulccsal |

A lánc végén a `{queue}-dlx` exchange és a `{queue}-dlx` queue áll, "" kulccsal összekötve.

Egy hibára futó üzenet útja:

```mermaid
flowchart LR
  Q["{queue}"] -- reject --> W1["{queue}-wait-t0"] -- ttl --> R1["{queue}-retry-t0"]
  R1 -- reject --> W2["{queue}-wait-t1"] -- ttl --> R2["{queue}-retry-t1"]
  R2 -- reject --> D["{queue}-dlx"]
```

- **Próbálkozások:** összesen 1 + az intervallumok száma. Az utolsó után az üzenet a `{queue}-dlx` queue-ban marad.
- **Routing key.** A dead-letterelt üzenet megtartja a routing key-ét. A 0.2-ben a retry kötések csak "" kulcsot használtak, ezért a nem üres routing key-jel publikált üzenetek az első retrynél elvesztek. A 0.3-tól a retry lánc a beállított routing key-jel is kötve van; az ettől eltérő kulcsú `RoutableEvent`-ek retryje továbbra sem működik.
- **A retry ki-bekapcsolása megtöri az indulást.** A fő queue argumentumai eltérnek retryvel és anélkül. A beállítás megváltoztatása után egy már létező queue újradeklarálása `PRECONDITION_FAILED` hibával elbukik.

## Üzenetek a dróton

| Tulajdonság | Command | Query | Event |
|---|---|---|---|
| Exchange | a beállított exchange | `""` (default exchange) | a beállított exchange |
| Routing key | a beállított routing key | a beállított *exchange* neve | `RoutableEvent.RoutingKey`, vagy a beállított routing key |
| `Type` | `"<FullName>, <AssemblyName>"` | ugyanígy | ugyanígy, `RoutableEvent` esetén a belső event típusa |
| `MessageId` | | | `EventId` |
| `CorrelationId` | az aktuális `CorrelationId`, vagy új GUID | ugyanígy | ugyanígy |
| `ReplyTo` | | szerver által elnevezett, exkluzív válaszqueue | |
| `causation_id` header | az aktuális `CurrentId` (string), csak ha be van állítva | ugyanígy | ugyanígy |
| Body | UTF-8 JSON (alapértelmezett Newtonsoft beállítások) | ugyanígy | `RoutableEvent` esetén a belső event |

Nincs beállítva `ContentType`, `DeliveryMode` (vagyis az üzenetek nem perzisztensek) és időbélyeg.

### Fogadás

- A consumerek kézi ackkal futnak, alapból **prefetch-korlát nélkül** (`BasicQos` csak akkor hívódik, ha a `RabbitMessagingOptions.PrefetchCount` be van állítva, 0.3).
- Minden kézbesítésnél a consumer:
  1. új lifetime scope-ot nyit;
  2. a `Type` property (vagy beágyazott `$type`) alapján feldolgozza a body-t;
  3. beállítja a correlationt;
  4. végigviszi az üzenetet a pipeline-on.
- Siker esetén ackol.
- Query esetén az ack után a JSON eredményt a `ReplyTo` címre publikálja, a kérés `CorrelationId`-jával. A `null` eredményre a 0.3-tól `null` válasz megy (a 0.2 nem válaszolt). A válaszküldés hibáját logolja, és nem rejecteli a már ackolt üzenetet (0.3).
- **Bármilyen** kivételnél `requeue: false` paraméterrel rejectel. Az üzenet a retry láncba kerül, ha van ilyen, egyébként eldobódik. A query hívója ilyenkor nem kap választ, kivéve ha a `ReplyOnQueryFailure` be van kapcsolva (opt-in, 0.3).
- A nem command/query/event body-kat egy `IReceiveRabbitMessage` kapja meg, ha regisztráltál ilyet.

### Query RPC (küldő oldal)

1. Deklarál egy szerver által elnevezett, exkluzív válaszqueue-t, és auto-ackkal fogyasztja.
2. Elküldi a queryt, és megvárja az azonos `CorrelationId`-jú választ.
3. Törli a válaszqueue-t, és deserializálja az eredményt.

Alapból **nincs timeout**, ezért adj át `CancellationToken`-t, vagy állítsd be a `RabbitMessagingOptions.QueryTimeoutInSeconds`-t (0.3).

## Üzenetkezelési beállítások (0.3)

A `RabbitMessagingOptions` egy regisztrált példányból vagy a `ServiceComponents:RabbitMessagingOptions` sectionből olvasódik. A beállítások: `QueryTimeoutInSeconds`, `PrefetchCount` és `ReplyOnQueryFailure`. A `ReplyOnQueryFailure` bekapcsolása előtt olvasd el a [migrációs jegyzetet](../migration/0.2-to-0.3.md#opt-in-változások).

## Alacsonyabb szintű regisztráció

Az `AddRabbit` által használt elemek publikusak:

| Metódus | Mit regisztrál |
|---|---|
| `AddRabbitConnection(uri, clientName, key)` | Szinkron kapcsolat, az első feloldáskor csatlakozik |
| `AddRabbitChannel(connectionKey, key)` | Channel az adott kapcsolaton |
| `AddRabbitConsumer(queue, consumerTag, channelKey, consumerKey)` | Consumer |
| `AddRabbitReceivers()` | Rabbit receiverek |
| `AddRabbitReceiverCorrelationBehavior()` | Correlation az üzenet property-kből |
| `AddRabbitCommandSender(exchange, routingKey, channelKey, key)` | Command sender |
| `AddRabbitQuerySender(exchange, routingKey, channelKey, key)` | Query sender |
| `AddRabbitEventPublisher(exchange, routingKey, mandatory, basicProperties, channelKey, key)` | Event publisher (a `mandatory` és a `basicProperties` sablon a 0.3-tól érvényesül; a 0.2 figyelmen kívül hagyta) |
| `AddRabbitSenderCorrelationBehavior()` | Correlation a kimenő üzenetekre |
| `AddRabbitRetryConsumers(connectionKey, queue, ttls, clientName)` | Retry consumerek |
| `IChannel.AddRabbitRetry(scope, queue, ttls)` | Deklarálja a retry topológiát |
| `IChannel.AddRabbitRetry(scope, queue, ttls, routingKey)` | Ugyanez, a routing key-jel is kötve (0.3) |

### Aszinkron inicializálás

Új kódban a kapcsolatok és a channelek a container felépítése után, aszinkron módon is megnyithatók. Így a regisztráció nem végez hálózati műveletet:

```csharp
builder.RegisterRabbitConnection(new Uri("amqp://localhost"), "client");
builder.RegisterRabbitChannel();
var container = builder.Build();
await container.InitializeRabbitAsync(cancellationToken);   // az IConnection / IChannel feloldása előtt
```

- **Párhuzamos hívás:** az egyidejű hívások egyetlen inicializálást osztanak meg.
- **Hiba:** hiba esetén a félig megnyitott erőforrások lezárulnak.
- **Lezárás:** a container lezárásakor a channelek a kapcsolatok előtt záródnak be.
- **Túl korai feloldás:** ha inicializálás előtt oldasz fel, kivételt kapsz, és az üzenete megnevezi az `InitializeRabbitAsync`-et.

## Kapcsolati beállítások

A beállítások a `ServiceComponents:RabbitConnectionOptions` sectionből kötődnek. A kódban megadott URI és kliensnév felülírja a konfigurációt.

| Beállítás | Alapérték | Megjegyzés |
|---|---|---|
| `EndpointUri` | kötelező | |
| `ClientName` | kötelező | |
| `AutomaticRecoveryEnabled` | `true` | |
| `HandshakeContinuationTimeoutInSeconds` | `120` | |
| `RequestedHeartbeatInSeconds` | `60` | |
| `RequestedChannelMaxInSeconds` | `2048` | A neve ellenére channelszám |
| `NetworkRecoveryIntervalInSeconds` | `5` | |
| `Ssl` | `false` | Csak `true` értéknél érvényesül (0.3); a 0.2 felülírta az URI-beli `amqps`-t |
| `VirtualHost` | `/` | Csak `/`-tól eltérő értéknél érvényesül (0.3); a 0.2 felülírta az URI-beli vhostot |
