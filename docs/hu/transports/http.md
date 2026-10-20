**Magyar** | [English](../../en/transports/http.md)

# HTTP transport

## Fogadás

Minden szolgáltatás egyetlen endpointon fogadja az összes üzenetét, ez megállapodás szerint a `POST /api/generic` (lásd a `GenericController`-t az [Első lépések](../getting-started.md) oldalon). A fogadó oldal regisztrálása:

```csharp
builder.AddHttpRequestParser();               // HttpRequestParser (a UseDefault elvégzi)
builder.AddHttpReceivers();                   // IReceiveHttpCommand / Query / Event
builder.AddHttpReceiverCorrelationBehavior(); // correlation a headerekből
```

### Kérés

| Rész | Tartalom |
|---|---|
| Metódus | `POST` |
| `domain-type` header | Az üzenet típusa: `"<FullName>, <AssemblyName>"`, például `Orders.Api.PlaceOrder, Orders.Api` (verzió nélkül) |
| `correlation-id` header | Opcionális. Ha hiányzik, új GUID keletkezik. |
| `causation-id` header | Opcionális. A kiváltó üzenet azonosítója. |
| Body | Az üzenet JSON-ként (Newtonsoft.Json, a property-k deklarált nevével). Üres body esetén `{}`. A content type nem számít. |

**`domain-type` nélkül** a body-nak tartalmaznia kell egy Newtonsoft `$type` property-t. A 0.3-tól csak ezek a típusok szerepelhetnek benne: üzenettípusok, az `AddMediator`-nak vagy a `RequestParser.AllowTypesFrom`-nak átadott assembly-k típusai, egy üzenet mellett deklarált típusok, valamint egyszerű .NET érték- és gyűjteménytípusok. Minden mást 400-zal utasít el.

A headerek neve a `HttpRequestOptions`-szel (`DomainTypeHeaderKey`, `CorrelationIdHeaderKey`, `CausationIdHeaderKey`) változtatható meg, ha `IOptions<HttpRequestOptions>`-ként regisztrálod.

### Válasz

| Eredmény | Státusz | Body |
|---|---|---|
| Sikeres command vagy event | 200 | üres |
| Sikeres query | 200 | Az eredmény, a host MVC formatterével kiírva. Az `AddNewtonsoftJson()` alapból camelCase-t használ. A `string` eredmény `text/plain`-ként megy ki, a `null` eredmény üres body-t ad. |
| Hiba | lásd lent | JSON hiba |

### Hibaformátum

Az `ErrorHandlingMiddleware` minden kivételt elkap, és az `IExceptionMapperService`-szel íratja ki a választ. A beépített megvalósítás a `JsonExceptionMapper`, ezt neked kell regisztrálnod:

```csharp
container.RegisterType<JsonExceptionMapper>().As<IExceptionMapperService>().SingleInstance();
```

A kivételeket ebben a sorrendben vizsgálja:

| Kivétel | Státusz | Body |
|---|---|---|
| `UnauthorizedAccessException` | 401 | `{"Type":"security","ErrorMessage":"…"}` |
| `NotImplementedException` | 501 | `{"Type":"generic","ErrorMessage":"…"}` |
| `NotFoundException` | 404 | `{"Type":"generic","ErrorMessage":"…"}` |
| `BusinessException` és leszármazottai (`ConflictException`, `GoneException`, `CommandConstraintException`) | 400 | `{"Type":"business","ErrorCode":<int>,"ErrorMessage":"…"}` |
| FluentValidation `ValidationException` | 400 | `{"Type":"validation","Errors":[{"PropertyName":"…","ErrorCode":"…","ErrorMessage":"…","AttemptedValue":…}],"ErrorMessage":"Validation failed"}` |
| `InvalidOperationException`, ide tartozik az ismeretlen típus és a hiányzó handler is | 400 | `{"Type":"generic","ErrorMessage":"…"}` |
| minden más | 500 | `{"Type":"generic","ErrorMessage":"Something really bad happened"}` |

- A property-nevek PascalCase-ek, a content type `application/json`.
- Logolás (0.3; a 0.2 nem logolt): az 5xx-re leképzett hibák `Error`, a többi `Debug` szinten kerülnek a logba. A handler hibáit a log behavior is logolja.
- Ha a válasz már elindult, a middleware nem tudja lecserélni: logolja a hibát, és továbbdobja a kivételt.

A `JsonExceptionMapper(responseMapper, exceptionMapper)` konstruktor mindkét paramétere lecserélhető saját leképezésre.

## Küldés

```csharp
// A command sendert, a query sendert és az event publishert regisztrálja "http" kulccsal.
hostBuilder.AddHttpSender(configuration => new Uri(configuration["Orders:Uri"]), "http");

// Vagy egyenként a container builderen. A httpClientKey nevesített HttpClientet választ.
builder.AddHttpCommandSender(uri, key: "http", httpClientKey: "orders");
builder.AddHttpQuerySender(uri, "http");
builder.AddHttpEventPublisher(uri, "http");
builder.AddHttpSenderCorrelationBehavior();   // a UseDefault (AddCorrelation) elvégzi
```

A senderekhez kell az `IHttpClientFactory` (`services.AddHttpClient()`, a `UseDefault` elvégzi), az `IOptions<HttpRequestOptions>` és az `IExceptionMapperService`.

### A kimenő kérés

| Rész | Érték |
|---|---|
| Kérés | `POST <uri>` |
| Body | `JsonConvert.SerializeObject(message)`, `text/plain; charset=utf-8`-ként |
| `domain-type` header | Az üzenet típusa, mint fent |
| `correlation-id` header | Csak akkor, ha az aktuális scope-nak van ilyen |
| `causation-id` header | Az aktuális scope `CurrentId`-ja; ha null, nem kerül a drótra |
| `Accept` header | `application/json`, kérésenként beállítva (a 0.2 minden hívásnál hozzáfűzte a kliens alapértelmezett headereihez) |

### A válasz feldolgozása

Sikeres (2xx) válasz esetén a query eredményét Newtonsoft.Json deserializálja. Ez kis- és nagybetűre nem érzékeny, így a camelCase és a PascalCase is működik.

A nem 2xx válaszokat a `JsonExceptionMapper.ThrowExceptionIfNeeded` alakítja vissza kivétellé:

| Válasz | Kivétel |
|---|---|
| 401 | `UnauthorizedAccessException` |
| 501 | `NotImplementedException` |
| 404 | `NotFoundException` |
| 400 + business | `BusinessException(code, message)` |
| 400 + validation | `ValidationException` a hibákkal |
| egyéb 400 | `InvalidOperationException` |
| minden más (az 500 is) | `Exception("Unknown error")` |

Az üres vagy nem hiba-JSON body-kat (például egy JWT challenge üres 401-es válaszát) a státuszkód alapján képezi le (0.3; a 0.2 `JsonReaderException`-t dobott): a 401, a 404 és az 501 a fenti módon, a 400 pedig a body szövegével `InvalidOperationException` lesz.

## Authorizáció

Az `AddHttpReceiverAuthorizationBehavior()` a **handler osztály** `[Authorize]` attribútumait ellenőrzi az aktuális `HttpContext.User` alapján.

- Ha az ellenőrzés nem sikerül, `UnauthorizedAccessException`-t dob, és a válasz 401 lesz. Az üzenet a nem teljesült követelmények listája.
- Eventekre nincs authorizáció.
- **A correlation megmarad** (0.3). A 0.2-ben a HTTP correlation behavior kimaradt, amíg ez a behavior be volt kapcsolva.
- A handler osztályát a regisztrációból olvassa ki, így az ellenőrzéshez nem példányosítja a handlert.

```csharp
[Authorize(Roles = "admin")]
public class SecuredCommandHandler : IHandleCommand<SecuredCommand> { ... }
```
