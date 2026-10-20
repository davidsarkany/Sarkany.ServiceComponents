[Magyar](../../hu/transports/http.md) | **English**

# HTTP transport

## Receiving

Each service exposes one endpoint for all its messages, conventionally `POST /api/generic` (see `GenericController` in [Getting started](../getting-started.md)). Register the HTTP receive side with:

```csharp
builder.AddHttpRequestParser();               // HttpRequestParser (UseDefault does this)
builder.AddHttpReceivers();                   // IReceiveHttpCommand / Query / Event
builder.AddHttpReceiverCorrelationBehavior(); // correlation from headers
```

### Request

| Part | Content |
|---|---|
| Method | `POST` |
| Header `domain-type` | Message type: `"<FullName>, <AssemblyName>"`, e.g. `Orders.Api.PlaceOrder, Orders.Api` (no version) |
| Header `correlation-id` | Optional. A new GUID is generated when missing. |
| Header `causation-id` | Optional. Id of the causing message. |
| Body | The message as JSON (Newtonsoft.Json, property names as declared). An empty body means `{}`. Content type is ignored. |

**Without `domain-type`** the body must carry a Newtonsoft `$type` property. In Sarkany, the named types are restricted to message types, the assemblies passed to `AddMediator` or `RequestParser.AllowTypesFrom`, types declared next to a message, and plain .NET value and collection types; anything else is rejected with 400.

You can rename the headers with `HttpRequestOptions` (`DomainTypeHeaderKey`, `CorrelationIdHeaderKey`, `CausationIdHeaderKey`) registered as `IOptions<HttpRequestOptions>`.

### Response

| Outcome | Status | Body |
|---|---|---|
| Command or event handled | 200 | empty |
| Query handled | 200 | Result, written by the host's MVC formatter. `AddNewtonsoftJson()` uses camelCase by default. A `string` result is written as `text/plain`. A `null` result gives an empty body. |
| Error | see below | JSON error |

### Error contract

`ErrorHandlingMiddleware` catches every exception and lets `IExceptionMapperService` write the response. The built-in implementation is `JsonExceptionMapper`, and you have to register it yourself:

```csharp
container.RegisterType<JsonExceptionMapper>().As<IExceptionMapperService>().SingleInstance();
```

Exceptions are matched in this order:

| Exception | Status | Body |
|---|---|---|
| `UnauthorizedAccessException` | 401 | `{"Type":"security","ErrorMessage":"…"}` |
| `NotImplementedException` | 501 | `{"Type":"generic","ErrorMessage":"…"}` |
| `NotFoundException` | 404 | `{"Type":"generic","ErrorMessage":"…"}` |
| `BusinessException` and subclasses (`ConflictException`, `GoneException`, `CommandConstraintException`) | 400 | `{"Type":"business","ErrorCode":<int>,"ErrorMessage":"…"}` |
| FluentValidation `ValidationException` | 400 | `{"Type":"validation","Errors":[{"PropertyName":"…","ErrorCode":"…","ErrorMessage":"…","AttemptedValue":…}],"ErrorMessage":"Validation failed"}` |
| `InvalidOperationException`, including unknown types and missing handlers | 400 | `{"Type":"generic","ErrorMessage":"…"}` |
| anything else | 500 | `{"Type":"generic","ErrorMessage":"Something really bad happened"}` |

- Property names are PascalCase.
- The content type is `application/json`.
- Logging (Sarkany; the original did not log): failures mapped to 5xx are logged at `Error`, other failures at `Debug`. A handler failure is also logged by the log behavior.
- If the response has already started, the middleware cannot replace it: it logs the failure and rethrows.

Both constructor arguments of `JsonExceptionMapper(responseMapper, exceptionMapper)` can be replaced with your own mapping.

## Sending

```csharp
// Registers the command sender, query sender and event publisher under the key "http".
hostBuilder.AddHttpSender(configuration => new Uri(configuration["Orders:Uri"]), "http");

// Or one by one on the container builder. httpClientKey selects a named HttpClient.
builder.AddHttpCommandSender(uri, key: "http", httpClientKey: "orders");
builder.AddHttpQuerySender(uri, "http");
builder.AddHttpEventPublisher(uri, "http");
builder.AddHttpSenderCorrelationBehavior();   // UseDefault (AddCorrelation) does this
```

The senders need `IHttpClientFactory` (`services.AddHttpClient()`; `UseDefault` does this), `IOptions<HttpRequestOptions>` and `IExceptionMapperService`.

### What a sender puts on the wire

| Part | Value |
|---|---|
| Request | `POST <uri>` |
| Body | `JsonConvert.SerializeObject(message)`, sent as `text/plain; charset=utf-8` |
| Header `domain-type` | The message type, as above |
| Header `correlation-id` | Only when the current scope has one |
| Header `causation-id` | The `CurrentId` of the current scope. Not sent when it is null. |
| Header `Accept` | `application/json`, set per request (the original appended it to the client's default headers on every call) |

### How responses are handled

- **Success (2xx):** query results are deserialized with Newtonsoft.Json. Matching is case-insensitive, so camelCase and PascalCase both work.
- **Error:** non-2xx responses go back through `JsonExceptionMapper.ThrowExceptionIfNeeded`.

| Response | Exception thrown |
|---|---|
| 401 | `UnauthorizedAccessException` |
| 501 | `NotImplementedException` |
| 404 | `NotFoundException` |
| 400 + business | `BusinessException(code, message)` |
| 400 + validation | `ValidationException` with the failures |
| other 400 | `InvalidOperationException` |
| anything else, including 500 | `Exception("Unknown error")` |

Bodies that are empty or not error JSON, e.g. the empty 401 of a JWT challenge, are mapped by status code (Sarkany; the original threw `JsonReaderException`): 401, 404 and 501 as above, 400 as `InvalidOperationException` with the body text.

## Authorization

`AddHttpReceiverAuthorizationBehavior()` checks the `[Authorize]` attributes of the **handler class** against the current `HttpContext.User`.

- **On failure** it throws `UnauthorizedAccessException`. The message is the list of failed requirements, and the response is 401.
- **Events** are not authorized.
- **Correlation is kept** (Sarkany). In the original the HTTP correlation behavior was bypassed while this behavior was enabled.
- The handler class is taken from its registration, so the handler is not instantiated just for the check.

```csharp
[Authorize(Roles = "admin")]
public class SecuredCommandHandler : IHandleCommand<SecuredCommand> { ... }
```
