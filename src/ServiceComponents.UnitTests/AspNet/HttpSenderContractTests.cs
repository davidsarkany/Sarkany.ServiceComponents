using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Autofac;
using FluentValidation;
using Microsoft.Extensions.Options;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Senders;
using ServiceComponents.AspNet.Exceptions;
using ServiceComponents.AspNet.Http;
using ServiceComponents.AspNet.Http.Senders;
using ServiceComponents.Core.Exceptions;
using ServiceComponents.Infrastructure.CorrelationContext;
using ServiceComponents.UnitTests.Fixtures;
using Xunit;
using HttpRequestOptions = ServiceComponents.AspNet.Http.HttpRequestOptions;

namespace ServiceComponents.UnitTests.AspNet
{
    [Collection("prometheus")]
    public class HttpSenderContractTests : IAsyncLifetime
    {
        private HttpTestHost _host;
        private IContainer _sender;
        private TestServerHttpClientFactory _clients;

        public Task InitializeAsync()
        {
            _host = new HttpTestHost();
            _clients = new TestServerHttpClientFactory(_host.Server);
            _sender = BuildSender(_clients);
            return Task.CompletedTask;
        }

        private static IContainer BuildSender(IHttpClientFactory clients, object key = default, string httpClientKey = default)
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance(clients).As<IHttpClientFactory>();
            builder.RegisterInstance(Options.Create(new HttpRequestOptions()));
            builder.RegisterType<JsonExceptionMapper>().As<IExceptionMapperService>();
            builder.AddCorrelationInfo();
            var uri = new Uri("http://localhost/api/generic");
            builder.AddHttpCommandSender(uri, key, httpClientKey);
            builder.AddHttpQuerySender(uri, key, httpClientKey);
            builder.AddHttpEventPublisher(uri, key, httpClientKey);
            builder.AddHttpSenderCorrelationBehavior();
            return builder.Build();
        }

        public async Task DisposeAsync()
        {
            await _sender.DisposeAsync();
            await _host.DisposeAsync();
        }

        private static string TypeName<T>() => $"{typeof(T).FullName}, {typeof(T).Assembly.GetName().Name}";

        [Fact]
        public async Task Command_request_headers_and_correlation_roundtrip()
        {
            await using var scope = _sender.BeginLifetimeScope();
            var correlation = scope.Resolve<Correlation>();
            correlation.CorrelationId = "corr";
            correlation.CurrentId = "parent";

            await scope.Resolve<ISendCommand>().SendAsync(new HttpCommand("ok", "c-1"));

            Assert.Equal($"Accept=application/json|causation-id=parent|Content-Type=text/plain; charset=utf-8|correlation-id=corr|domain-type={TypeName<HttpCommand>()}",
                Assert.Single(_host.Headers));
            Assert.Equal(("command:ok", "corr", "parent", "c-1"), Assert.Single(_host.Handled));
        }

        [Fact]
        public async Task Missing_correlation_omits_correlation_and_causation_headers()
        {
            await _sender.Resolve<ISendCommand>().SendAsync(new HttpCommand("ok"));

            // causation-id is always added to the content headers, but with a null value it never reaches the wire.
            Assert.Equal($"Accept=application/json|Content-Type=text/plain; charset=utf-8|domain-type={TypeName<HttpCommand>()}",
                Assert.Single(_host.Headers));
        }

        [Fact]
        public async Task Query_result_is_deserialized()
        {
            var result = await _sender.Resolve<ISendQuery>().SendAsync(new HttpQuery(5));

            Assert.Equal(10, result.Doubled);
            Assert.Equal("v5", result.Text);
        }

        [Fact]
        public async Task Null_query_result_is_default()
        {
            Assert.Null(await _sender.Resolve<ISendQuery>().SendAsync(new HttpQuery(0)));
        }

        [Fact]
        public async Task Events_are_posted_one_by_one()
        {
            await _sender.Resolve<IPublishEvent>().PublishAsync(new HttpEvent("a"));
            await _sender.Resolve<IPublishEvent>().PublishAsync(new IEvent[] { new HttpEvent("b"), new HttpEvent("c") });

            Assert.Equal(3, _host.Headers.Length);
            Assert.All(_host.Headers, h => Assert.Contains($"domain-type={TypeName<HttpEvent>()}", h));
        }

        [Fact]
        public async Task Routable_event_is_posted_as_the_wrapped_event()
        {
            await _sender.Resolve<IPublishEvent>().PublishAsync(new RoutableEvent(new HttpEvent("routed", "e-1"), "key"));

            // Fixed: the wrapper was posted, which the receiving service could not handle.
            Assert.Contains($"domain-type={TypeName<HttpEvent>()}", Assert.Single(_host.Headers));
            Assert.Equal("event:routed", Assert.Single(_host.Handled).what);
        }

        [Fact]
        public async Task Remote_exceptions_are_mapped_back()
        {
            var sender = _sender.Resolve<ISendCommand>();

            var business = await Assert.ThrowsAsync<BusinessException>(() => sender.SendAsync(new HttpCommand("business")));
            Assert.Equal((42, "Business rule violated"), (business.ErrorCode, business.Message));

            var notFound = await Assert.ThrowsAsync<NotFoundException>(() => sender.SendAsync(new HttpCommand("notfound")));
            Assert.Equal((404, "Thing not found"), (notFound.ErrorCode, notFound.Message));

            // Conflict is a BusinessException with code 0 on the wire, so the type is not preserved.
            var conflict = await Assert.ThrowsAsync<BusinessException>(() => sender.SendAsync(new HttpCommand("conflict")));
            Assert.Equal("Conflict!", conflict.Message);

            Assert.Equal("No access", (await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sender.SendAsync(new HttpCommand("unauthorized")))).Message);
            Assert.Equal("Later", (await Assert.ThrowsAsync<NotImplementedException>(() => sender.SendAsync(new HttpCommand("notimplemented")))).Message);
            Assert.Equal("Invalid state", (await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(new HttpCommand("invalid")))).Message);

            var validation = await Assert.ThrowsAsync<ValidationException>(() => sender.SendAsync(new HttpCommand("")));
            var failure = Assert.Single(validation.Errors);
            Assert.Equal(("Mode", "REQUIRED", "Mode is required", ""), (failure.PropertyName, failure.ErrorCode, failure.ErrorMessage, failure.AttemptedValue));

            // Server errors lose their details on purpose.
            var crash = await Assert.ThrowsAsync<Exception>(() => sender.SendAsync(new HttpCommand("crash")));
            Assert.Equal("Unknown error", crash.Message);
        }

        [Fact]
        public async Task Keyed_registration_uses_named_http_client()
        {
            var clients = new TestServerHttpClientFactory(_host.Server);
            await using var keyed = BuildSender(clients, "http", "my-client");

            Assert.False(keyed.IsRegistered<ISendCommand>());
            await keyed.ResolveKeyed<ISendCommand>("http").SendAsync(new HttpCommand("ok"));
            await keyed.ResolveKeyed<ISendQuery>("http").SendAsync(new HttpQuery(1));
            await keyed.ResolveKeyed<IPublishEvent>("http").PublishAsync(new HttpEvent("e"));

            Assert.Equal(new[] { "my-client", "my-client", "my-client" }, clients.RequestedNames);
            Assert.Equal(3, _host.Headers.Length);
        }

        [Fact]
        public async Task Reused_client_does_not_accumulate_accept_header()
        {
            var shared = _host.Server.CreateClient();
            await using var sender = BuildSender(new SingleClientFactory(shared));

            await sender.Resolve<ISendCommand>().SendAsync(new HttpCommand("ok"));
            await sender.Resolve<ISendCommand>().SendAsync(new HttpCommand("ok"));

            // Fixed: Accept is set per request instead of being appended to DefaultRequestHeaders on every call.
            Assert.Empty(shared.DefaultRequestHeaders.Accept);
        }

        private class SingleClientFactory : IHttpClientFactory
        {
            private readonly HttpClient _client;
            public SingleClientFactory(HttpClient client) => _client = client;
            public HttpClient CreateClient(string name) => _client;
        }

        [Fact]
        public async Task Non_json_or_untyped_error_bodies_map_by_status_code()
        {
            var mapper = new JsonExceptionMapper();
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad gateway</html>") };

            // Fixed (#13): a non-JSON error body no longer escapes as a JsonReaderException.
            Assert.Equal("Unknown error", (await Assert.ThrowsAsync<Exception>(() => mapper.ThrowExceptionIfNeeded(response))).Message);

            var noType = new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) { Content = new StringContent("{}") };
            Assert.Equal("{}", (await Assert.ThrowsAsync<InvalidOperationException>(() => mapper.ThrowExceptionIfNeeded(noType))).Message);

            var unknownType = new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) { Content = new StringContent("{\"type\":\"other\"}") };
            Assert.IsType<InvalidOperationException>(await Record.ExceptionAsync(() => mapper.ThrowExceptionIfNeeded(unknownType)));

            var emptyBadRequest = new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) { Content = new StringContent("") };
            Assert.Equal("Bad request", (await Assert.ThrowsAsync<InvalidOperationException>(() => mapper.ThrowExceptionIfNeeded(emptyBadRequest))).Message);
            await Assert.ThrowsAsync<NotFoundException>(() => mapper.ThrowExceptionIfNeeded(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { Content = new StringContent("not json") }));
            await Assert.ThrowsAsync<NotImplementedException>(() => mapper.ThrowExceptionIfNeeded(new HttpResponseMessage(System.Net.HttpStatusCode.NotImplemented)));
            var serverError = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) { Content = new StringContent("{\"Type\":\"generic\",\"ErrorMessage\":\"x\"}") };
            Assert.Equal("Unknown error", (await Assert.ThrowsAsync<Exception>(() => mapper.ThrowExceptionIfNeeded(serverError))).Message);

            await mapper.ThrowExceptionIfNeeded(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        }

        [Fact]
        public async Task Custom_mapper_functions_are_used()
        {
            var mapper = new JsonExceptionMapper(
                exception => (418, new GenericErrorResponse("teapot:" + exception.Message)),
                (status, content) => status == System.Net.HttpStatusCode.Conflict ? new ConflictException("mapped") : null);

            await Assert.ThrowsAsync<ConflictException>(() => mapper.ThrowExceptionIfNeeded(new HttpResponseMessage(System.Net.HttpStatusCode.Conflict)));
            // A null mapping result falls back to a generic exception.
            Assert.Equal("Unknown error", (await Assert.ThrowsAsync<Exception>(() => mapper.ThrowExceptionIfNeeded(new HttpResponseMessage(System.Net.HttpStatusCode.Gone)))).Message);

            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Response.Body = new System.IO.MemoryStream();
            await mapper.WriteResponse(new Exception("x"), context.Response);
            context.Response.Body.Position = 0;
            Assert.Equal(418, context.Response.StatusCode);
            Assert.Equal("application/json", context.Response.ContentType);
            Assert.Equal("{\"Type\":\"generic\",\"ErrorMessage\":\"teapot:x\"}", await new System.IO.StreamReader(context.Response.Body).ReadToEndAsync());
        }

        [Fact]
        public async Task Http_query_with_string_result_roundtrips_as_plain_text()
        {
            // Ok(string) is written as text/plain (no JSON quotes); Newtonsoft still reads it back as the string value.
            var result = await _sender.Resolve<ISendQuery>().SendAsync(new StringQuery());
            Assert.Equal("plain value", result);
        }

        [Fact]
        public void Request_options_defaults()
        {
            var options = new HttpRequestOptions();

            Assert.Equal(("domain-type", "correlation-id", "causation-id"), (options.DomainTypeHeaderKey, options.CorrelationIdHeaderKey, options.CausationIdHeaderKey));
        }
    }
}
