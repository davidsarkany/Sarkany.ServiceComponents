**Magyar** | [English](../en/README.md)

# ServiceComponents dokumentáció

> [!WARNING]
> **Nem éles használatra készült.** A projekt kizárólag tesztelési célokra jelenik meg. Semmilyen garanciát vagy
> támogatást nem nyújt, bármikor változhat vagy visszavonható. Éles rendszerekben ne használd.

A ServiceComponents .NET könyvtárcsomag üzenetvezérelt mikroszolgáltatások építéséhez. Te megírod a commandokat, queryket, eventeket és a handlereiket, a könyvtárak pedig elvégzik a többit:

- fogadják az üzeneteket HTTP-n, RabbitMQ-n vagy folyamaton belül;
- végigviszik őket egy pipeline-on (validáció, logolás, correlation, metrikák);
- a kimenő üzeneteket a megfelelő transportra irányítják.

| Téma | |
|---|---|
| [Áttekintés](overview.md) | Csomagok, architektúra, a kérések útja |
| [Első lépések](getting-started.md) | Szolgáltatás építése lépésről lépésre, a példaprojekt alapján |
| [Üzenetkezelési fogalmak](concepts/messaging.md) | Commandok, queryk, eventek, handlerek, hookok, routing, correlation |
| [HTTP transport](transports/http.md) | Generic endpoint, headerek, hibaformátum, HTTP senderek |
| [RabbitMQ transport](transports/rabbitmq.md) | Topológia, retry és dead-lettering, query RPC, kapcsolati beállítások |
| [Megfigyelhetőség](observability.md) | Logolás, metrikák, health checkek |
| [Konfiguráció](configuration.md) | Az összes Options osztály, section és alapérték |
| [Integrációk](integrations.md) | Redis, NHibernate, CouchDB |
| [Tesztelés](testing.md) | `ServiceComponents.Testing`, a repó tesztkészletei |
| [Wire protocol referencia](reference/wire-protocol.md) | Normatív leírás mindarról, amire más szolgáltatások építenek |
| [Ismert hibák](known-issues.md) | A jelenlegi verzió hibái, tesztekkel rögzítve |
| [Teljesítmény](performance.md) | Benchmarkok, load tesztek, összehasonlítás a baseline-nal |

## Példaprojekt

A [`samples/`](../../samples) mappában két szolgáltatás van, és mindkettő csak a publikus API-t használja:

- Az **Orders** HTTP-n fogad commandokat és queryket, az eventjeit pedig RabbitMQ-ra publikálja.
- A **Notifications** feldolgozza ezeket az eventeket, és HTTP-n lekérdezi az Orders-t.

Az egész egy paranccsal indítható:

```bash
docker compose -f samples/docker-compose.yml up --build
```

A dokumentáció kódrészletei a példaprojektből származnak.
