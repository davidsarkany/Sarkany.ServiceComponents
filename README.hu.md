[English](README.md) | **Magyar**

# ServiceComponents

.NET könyvtárak üzenetvezérelt mikroszolgáltatásokhoz:

- commandok, queryk és eventek handlerekkel;
- bővíthető pipeline (validáció, logolás, correlation, Prometheus metrikák);
- HTTP, RabbitMQ és folyamaton belüli transport.

Az összeállítás Autofackel és Seriloggal történik.

```csharp
new ServiceComponentsHostBuilder()
    .UseDefault(new[] { typeof(PlaceOrder).Assembly }, new[] { typeof(PlaceOrderHandler).Assembly })
    .AddRabbit("amqp://guest:guest@localhost:5672", "orders", "orders", "orders-events")
    .AddEventRouter(@event => "rabbit")
    .CreateHostBuilder(args);
```

> A [gsinka/servicecomponents](https://github.com/gsinka/servicecomponents) újraírása, `Sarkany.ServiceComponents.*` néven kiadva.
> Az eredeti `ServiceComponents.*` 0.2-es csomagok drop-in cseréje: ugyanazok az assembly-k, namespace-ek, publikus API és wire protocol.

**Átállás az eredeti csomagokról:** a csomag ID-ket kell lecserélni, a kód változatlan marad.

```xml
<!-- előtte -->
<PackageReference Include="ServiceComponents.AspNet" Version="0.2.0-beta.12" />
<!-- utána -->
<PackageReference Include="Sarkany.ServiceComponents.AspNet" Version="0.3.0-beta.1" />
```

Egy projektben (és az általa hivatkozott könyvtárakban) minden `ServiceComponents.*` csomagot cserélni kell: mindkettő ugyanazokat az assembly-ket tartalmazza, ezért nem keverhetők. Részletek: [migrációs jegyzet](docs/hu/migration/0.2-to-0.3.md#csomagcsere).

- **Dokumentáció:** [docs/hu](docs/hu/README.md) · [docs/en](docs/en/README.md)
- **A 0.3 változásai:** [CHANGELOG](CHANGELOG.md) · [migrációs jegyzet](docs/hu/migration/0.2-to-0.3.md)
- **Példaszolgáltatások:** [samples/](samples) (`docker compose -f samples/docker-compose.yml up --build`)
- **Tesztek:**
  - `dotnet test src/ServiceComponents.sln`
  - lefedettséggel: `bash build/coverage.sh`
  - az integrációs tesztekhez Docker kell
- **Teljesítmény:** a `bash perf/run.sh` összeveti a repót az eredeti 0.2.0-beta.12 csomagokkal ([részletek](docs/hu/performance.md))
- **Együttműködés:** a `bash build/interop.sh` egymással futtatja az eredeti csomagokból és a repóból buildelt példaszolgáltatásokat
- **Drop-in ellenőrzés:** a `bash build/dropin.sh` becsomagolja a repót, és a csomagokból buildeli és teszteli a példákat
- **CI / kiadás:** a pushok és a pull requestek lefuttatják a teszteket lefedettséggel, az interopot és a drop-in ellenőrzést (`.github/workflows/ci.yml`). Egy verzió tag (`0.3.0-beta.1` vagy `v0.3.0`) pusholása ugyanezeket futtatja, majd kiadja a csomagokat a nuget.org-ra, és GitHub release-t készít (`.github/workflows/cd.yml`).
