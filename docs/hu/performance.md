**Magyar** | [English](../en/performance.md)

# Teljesítmény

A teljesítménytesztek ugyanazt a kódot futtatják két buildre, ugyanazon a gépen:

| Build | Mi ez | Honnan jön |
|---|---|---|
| **Baseline** | Az eredeti `ServiceComponents.*` csomagok `0.2.0-beta.12` verziója (az utolsó kiadás az újraírás előtt) | a nuget.org-ról |
| **Current** | A repó aktuális forrása | projekt-referenciák |

A `samples/` és a `perf/` projektek a `-p:ScTarget=Baseline|Current` kapcsolóval választanak buildet (lásd `samples/Directory.Build.props`). Az alábbi eredmények az újraírás közben készültek, ugyanezzel a 0.2.0-beta.12 forrással, lokálisan csomagolva.

## Futtatás

```bash
bash perf/run.sh                       # teljes futás, kb. 30–40 perc
PERF_QUICK=1 bash perf/run.sh          # a csomag gyors ellenőrzése, zajos számokkal
PERF_FILTER='*Mediator*' PERF_SCENARIOS=http bash perf/run.sh
```

- A riport a `perf/results/<időbélyeg>/README.md` fájlba kerül.
- Ha egy load teszt túllépi a küszöböt, a script nem nulla kilépési kóddal áll le.
- Futás előtt zárd be a többi alkalmazást.
- Csak ugyanazon a gépen készült futásokat hasonlíts össze. A CI runnerek túl zajosak ahhoz, hogy regresszióról döntsenek.

## Mikrobenchmarkok (`perf/ServiceComponents.Benchmarks`, BenchmarkDotNet)

Minden benchmark egy futáson belül mindkét buildre lefut, az arányok alapja a Baseline. A memóriafoglalást is mérik.

| Osztály | Mit mér |
|---|---|
| `MediatorBenchmarks` | Command, query és event (2 handlerrel) útja receiveren, behavior-ökön, dispatcheren és handleren át, új lifetime scope-ban. `Bare`: csak a dispatcherek. `Full`: a `UseDefault` + Prometheus pipeline. |
| `ParserBenchmarks` | `RequestParser` típusnévvel (kis payload és 200 tételes payload), illetve beágyazott `$type`-pal |
| `RabbitBenchmarks` | Command küldése, event publikálása, command fogadása a teljes pipeline-on át. A hamis `IChannel` azonnal végez, így nincs broker I/O. |
| `HttpBenchmarks` | Command és query fogadása `UseDefault` hoston, TestServeren át (MVC, parser, pipeline); a HTTP sender és a receiver oda-vissza útja |
| `StartupBenchmarks` | Egy `UseDefault` host felépítése és indítása |

## Load tesztek (`perf/ServiceComponents.LoadTests`)

Zárt hurkú terhelésgenerátor:

- N worker küldi egymás után a kéréseket;
- egy bemelegítő szakasz után 30 másodpercig mér (`--quick` esetén 10 másodpercig);
- a percentiliseket az összes mért késleltetésből pontosan számolja.

| Szcenárió | Mi fut |
|---|---|
| `http-command (PlaceOrder)` | Az Orders példa Kestrelen: HTTP, parser, teljes pipeline, memóriabeli tároló, loopback event |
| `http-query (GetOrder)` | Ugyanez a host, JSON-t visszaadó query |
| `rabbit-command round trip` | Küldés RabbitMQ-n (Testcontainers), fogadás, teljes pipeline, handler |
| `rabbit-query RPC` | Query RPC RabbitMQ-n (az exchange nevét viselő queue-val), legfeljebb 8 párhuzamos hívással |

NBomber helyett a generátor a repó része, mert az NBomber szervezeti használata licenchez kötött.

## Regressziós küszöbök

Egy újraírt csomag csak akkor kerül be, ha a Baseline-hoz képest ezeken belül marad:

| Terület | Mérőszám | Küszöb |
|---|---|---|
| Mikrobenchmark | átlagidő | legfeljebb +5% |
| Mikrobenchmark | memóriafoglalás | nem nőhet |
| Load teszt | átbocsátás | legfeljebb −5% |
| Load teszt | p99 késleltetés | legfeljebb +10% |

A `perf/run.sh` a load tesztek küszöbeit automatikusan ellenőrzi. A mikrobenchmarkok arányait a riportból kell kiolvasni.

**A tudatos költség nem regresszió.** Egy javítás vagy biztonsági ellenőrzés többe kerülhet, mint a régi, hibás viselkedés. Ez akkor elfogadható, ha mindhárom feltétel teljesül:

- abszolút értékben kicsi;
- éles üzemben nem forró útvonalon van;
- szerepel az alábbi táblázatban és a migrációs jegyzetben.

Csak azért, hogy egy ilyen eset a küszöb alá kerüljön, ne vezess be cache-t vagy más bonyolítást.

| Benchmark | Költség | Ok |
|---|---|---|
| Parser, beágyazott `$type` | +11% (kb. 78 ns 0,7 µs-on) | Minden `$type` allowlist-ellenőrzése (biztonsági javítás). A senderek ezt az utat nem használják. |
| Mediator event, Bare pipeline | +8% foglalás (0,76 KB) | A gyerek scope örökli a szülő correlationjét (#1-es javítás). A valódi `UseDefault` pipeline-ban a foglalás 14%-kal kisebb. |

## Eredmények

A számok 2026-10-19-én, az újraírás előtt készültek. A mérőgép: AMD Ryzen 7 5700X (8 mag, 16 szál), Windows 11, .NET 10.0.7.

Az újraírás előtt a Baseline és a Current kódja azonos, ezért a köztük lévő eltérés a mérés zaja.

### Mikrobenchmarkok (teljes futás)

| Benchmark | Baseline átlag | Foglalás | Current / Baseline |
|---|---:|---:|---:|
| Mediator command, Bare | 1,9 µs | 6,1 KB | 0,98 |
| Mediator command, teljes pipeline | 27,7 µs | 30,4 KB | 1,03 |
| Mediator query, teljes pipeline | 22,7 µs | 25,9 KB | 0,99 |
| Mediator event (2 handler), teljes pipeline | 21,7 µs | 26,5 KB | 1,00 |
| Parser, típusos, kicsi | 1,4 µs | 3,4 KB | 1,02 |
| Parser, típusos, 200 tétel | 90,6 µs | 65,4 KB | 0,96 |
| Parser, beágyazott `$type` | 0,7 µs | 3,4 KB | 1,01 |
| RabbitMQ command küldés | 4,8 µs | 10,2 KB | 1,00 |
| RabbitMQ command fogadás, teljes pipeline | 38,8 µs | 37,7 KB | 1,01 |
| HTTP command fogadás (TestServer) | 158 µs | 96 KB | 1,01 |
| HTTP query küldés és fogadás | 199 µs | 106 KB | 1,00 |
| `UseDefault` host felépítése és indítása | 5,5 ms | 3,2 MB | 1,03 |

**Zajszint**

- Az időben ±4%-on belül marad.
- A foglalás azonos. Egyetlen kivétel a HTTP fogadási út 2%-os eltérése, amelyet a két build eltérő assembly-verziói okoznak.

### Load tesztek

A számok 4 váltakozó kör mediánjai, körönként 32 workerrel és 30 másodperccel.

| Szcenárió | Átbocsátás | p50 | p99 | Current vs Baseline |
|---|---:|---:|---:|---|
| HTTP command (Orders Kestrelen, teljes pipeline) | 37 600 kérés/s | 0,70 ms | 2,7–4,1 ms | átbocsátás +0,1% |
| HTTP query | 53 300 kérés/s | 0,55 ms | 1,2 ms | átbocsátás +0,3% |
| RabbitMQ command round trip | 18 200 üzenet/s | 1,7 ms | 3,0 ms | átbocsátás −3,2% (egy kör) |
| RabbitMQ query RPC | 128 kérés/s | 62 ms | 69 ms | átbocsátás +1,2% (egy kör); a RabbitMQ újraírása után −1,8% (zaj) |

**Megjegyzések**

- **Több kör kell.** Egyetlen, egymás után futtatott A/B kör azonos kód mellett is akár 7%-kal eltért az átbocsátásban. Ezért fut a `perf/run.sh` alapból 3 váltakozó körrel, és ezért mediánokat hasonlít össze.
- **A HTTP command p99 zajos.** Körről körre 2,6 és 4,1 ms között ingadozik (GC). Több kör alapján érdemes megítélni, vagy a p95-öt nézni.
- **A RabbitMQ query RPC lassú.**
  - **Miért:** minden query a közös publisher channelen deklarál, fogyaszt és töröl egy saját válaszqueue-t. Ezek a channel-műveletek egymás után futnak, és queryként kb. 8 ms-ot vesznek igénybe. Az átbocsátás ezért a párhuzamosságtól függetlenül kb. 130 kérés/s: egy workerrel 8,6 ms, nyolccal 62 ms.
  - **Ami nem segített:** a blokkoló helyett aszinkron várakozás (0.3) ezen nem változtatott.
  - **Ami egy kompatibilis javításhoz kell:** channel-pool, channelenként tartós válaszqueue-val. Egyetlen közös válaszqueue nem működik, mert egy kérés párhuzamos queryi azonos `CorrelationId`-t visznek, a 0.2-es fogadók pedig csak ezt adják vissza.
  - **Terv:** opcionális fejlesztésként az újraírás után.

### A Core, a Domain és az Infrastructure újraírása után

Mindhárom benchmark-csomag egy teljes futásban ment le. Az arányok a Current-et viszonyítják a Baseline-hoz.

| Benchmark | Current / Baseline | Foglalás |
|---|---:|---:|
| Mediator command, teljes pipeline | 0,46 | 0,84 |
| Mediator query, teljes pipeline | 0,45 | 0,81 |
| Mediator event (2 handler), teljes pipeline | 0,44 | 0,86 |
| Mediator command, Bare | 0,80 | 0,98 |
| Mediator query, Bare | 0,83 | 0,97 |
| Mediator event (2 handler), Bare | 0,91 | 1,08 |
| RabbitMQ command fogadás, teljes pipeline | 0,43 | 0,86 |
| RabbitMQ command küldés / event publikálás | 0,97 / 0,98 | 1,00 |
| Parser, típusos, kicsi | 0,36 | 0,88 |
| Parser, típusos, 200 tétel | 0,99 | 0,99 |
| Parser, beágyazott `$type` | 1,05–1,11 | 1,00 |

Az interop ellenőrzés mind a négy párosításban sikeres.

### Az AspNet újraírása után

HTTP benchmarkok és HTTP load tesztek (3 váltakozó kör, 32 worker), a Current a Baseline-hoz viszonyítva:

| Benchmark / szcenárió | Current / Baseline | Foglalás |
|---|---:|---:|
| HTTP command / query fogadás | 0,81 / 0,79 | 0,94 |
| HTTP command / query küldés (sender + receiver) | 0,82 / 0,81 | 0,95 / 0,97 |
| Load: HTTP command | throughput +21,8%, p99 −14,7% | |
| Load: HTTP query | throughput +19,3%, p99 −10,2% | |

Az integrációs csomagok (Redis, NHibernate, CouchDB, Host) nincsenek mért útvonalon; a változásaikat csak tesztek fedik.

### Végső összehasonlítás (minden csomag újraírva)

Teljes futás, `perf/results/20261020-082855`: minden benchmark, HTTP és RabbitMQ load tesztek, 3 váltakozó kör. Az arányok: Current / Baseline.

| Benchmark | Idő | Foglalás |
|---|---:|---:|
| Mediator command / query / event, teljes pipeline | 0,47 / 0,42 / 0,45 | 0,84 / 0,81 / 0,85 |
| Mediator command / query / event, Bare | 0,85 / 0,85 / 0,88 | 0,98 / 0,97 / 1,08 |
| HTTP command / query fogadás | 0,79 / 0,77 | 0,94 / 0,92 |
| HTTP command / query küldés (sender + receiver) | 0,85 / 0,83 | 0,95 |
| RabbitMQ command fogadás, teljes pipeline | 0,44 | 0,85 |
| RabbitMQ command küldés / event publikálás | 0,99 / 1,04 | 1,00 |
| Parser, típusos kicsi / 200 tétel / beágyazott `$type` | 0,34 / 0,97 / 1,02 | 0,88 / 0,99 / 1,00 |
| `UseDefault` host felépítése és indítása | 0,94 | 0,98 |

| Load teszt | Throughput | p99 |
|---|---:|---:|
| HTTP command | +28,0% | −38,8% |
| HTTP query | +23,0% | −12,3% |
| RabbitMQ command oda-vissza | +2,1% | −1,4% |
| RabbitMQ query RPC | −0,4% | +1,3% |

Minden érték a küszöbön belül van. A Bare event dispatch +8%-os foglalása és a `$type` allowlist ellenőrzés költsége a dokumentált, tudatos költségek.

### Együttműködés

A `build/interop.sh` mind a négy Baseline/Current párosításban elindítja az Orders és a Notifications példát:

- az Orders RabbitMQ-n publikálja az `OrderPlaced` eventet;
- a Notifications feldolgozza, majd HTTP-n lekérdezi az Orders-t.

Mind a négy párosítás sikeres.
