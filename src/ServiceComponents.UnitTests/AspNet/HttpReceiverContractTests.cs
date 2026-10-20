using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ServiceComponents.UnitTests.Fixtures;
using Xunit;

namespace ServiceComponents.UnitTests.AspNet
{
    [Collection("prometheus")]
    public class HttpReceiverContractTests : IAsyncLifetime
    {
        private HttpTestHost _host;

        public Task InitializeAsync()
        {
            _host = new HttpTestHost();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync() => await _host.DisposeAsync();

        private static string TypeName<T>() => $"{typeof(T).FullName}, {typeof(T).Assembly.GetName().Name}";

        private async Task<(HttpStatusCode status, string contentType, string body)> Post(string json, string domainType, params (string key, string value)[] headers)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/generic") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if (domainType != null) {
                request.Headers.Add("domain-type", domainType);
            }

            foreach (var (key, value) in headers) {
                request.Headers.Add(key, value);
            }

            var response = await _host.CreateClient().SendAsync(request);
            return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Command_is_received_with_correlation_from_headers()
        {
            var (status, _, body) = await Post("{\"Mode\":\"ok\",\"CommandId\":\"c-1\"}", TypeName<HttpCommand>(), ("correlation-id", "corr"), ("causation-id", "cause"));
            Assert.True(status == HttpStatusCode.OK, body + string.Join(" || ", _host.Errors.Exceptions.Select(e => e.ToString())));

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("", body);
            Assert.Equal(new[] { ("command:ok", "corr", "cause", "c-1") }, _host.Handled);
        }

        [Fact]
        public async Task Missing_correlation_header_generates_one_and_causation_is_empty()
        {
            await Post("{\"Mode\":\"ok\",\"CommandId\":\"c-1\"}", TypeName<HttpCommand>());

            var handled = Assert.Single(_host.Handled);
            Assert.True(Guid.TryParse(handled.correlationId, out _));
            Assert.Null(handled.causationId);
        }

        [Fact]
        public async Task Query_result_is_returned_as_json()
        {
            var (status, contentType, body) = await Post("{\"Value\":21,\"QueryId\":\"q-1\"}", TypeName<HttpQuery>(), ("correlation-id", "corr"));

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("application/json", contentType);
            // The result body is produced by the host's MVC formatter (AddNewtonsoftJson defaults to camelCase here);
            // the HTTP query sender deserializes case-insensitively.
            Assert.Equal("{\"doubled\":42,\"text\":\"v21\"}", body);
            Assert.Equal(("query:21", "corr", null, "q-1"), Assert.Single(_host.Handled));
        }

        [Fact]
        public async Task Null_query_result_is_empty_ok()
        {
            var (status, _, body) = await Post("{\"Value\":0}", TypeName<HttpQuery>());

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("", body);
        }

        [Fact]
        public async Task Event_is_received()
        {
            var (status, _, _) = await Post("{\"Data\":\"e\",\"EventId\":\"e-1\"}", TypeName<HttpEvent>(), ("correlation-id", "corr"));

            Assert.Equal(HttpStatusCode.OK, status);
            // Fixed in 0.3 (known issues #1 and #20): handled once, with the correlation of the request.
            Assert.Equal(("event:e", "corr", (string)null, "e-1"), Assert.Single(_host.Handled));
        }

        [Fact]
        public async Task Embedded_type_is_accepted_without_domain_type_header()
        {
            var (status, _, _) = await Post("{\"$type\":\"" + TypeName<HttpCommand>() + "\",\"Mode\":\"ok\"}", null);

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("command:ok", Assert.Single(_host.Handled).what);
        }

        [Theory]
        [InlineData("business", HttpStatusCode.BadRequest, "{\"Type\":\"business\",\"ErrorCode\":42,\"ErrorMessage\":\"Business rule violated\"}")]
        [InlineData("conflict", HttpStatusCode.BadRequest, "{\"Type\":\"business\",\"ErrorCode\":0,\"ErrorMessage\":\"Conflict!\"}")]
        [InlineData("notfound", HttpStatusCode.NotFound, "{\"Type\":\"generic\",\"ErrorMessage\":\"Thing not found\"}")]
        [InlineData("unauthorized", HttpStatusCode.Unauthorized, "{\"Type\":\"security\",\"ErrorMessage\":\"No access\"}")]
        [InlineData("notimplemented", HttpStatusCode.NotImplemented, "{\"Type\":\"generic\",\"ErrorMessage\":\"Later\"}")]
        [InlineData("invalid", HttpStatusCode.BadRequest, "{\"Type\":\"generic\",\"ErrorMessage\":\"Invalid state\"}")]
        [InlineData("crash", HttpStatusCode.InternalServerError, "{\"Type\":\"generic\",\"ErrorMessage\":\"Something really bad happened\"}")]
        public async Task Exceptions_are_mapped_to_status_and_json(string mode, HttpStatusCode expectedStatus, string expectedBody)
        {
            var (status, contentType, body) = await Post("{\"Mode\":\"" + mode + "\"}", TypeName<HttpCommand>());

            Assert.Equal(expectedStatus, status);
            Assert.Equal("application/json", contentType);
            Assert.Equal(expectedBody, body);
        }

        [Fact]
        public async Task Validation_errors_are_mapped()
        {
            var (status, _, body) = await Post("{\"Mode\":\"\"}", TypeName<HttpCommand>());

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Equal("{\"Type\":\"validation\",\"Errors\":[{\"PropertyName\":\"Mode\",\"ErrorCode\":\"REQUIRED\",\"ErrorMessage\":\"Mode is required\",\"AttemptedValue\":\"\"}],\"ErrorMessage\":\"Validation failed\"}", body);
            Assert.Empty(_host.Handled);
        }

        [Fact]
        public async Task Unknown_request_is_bad_request()
        {
            var (status, _, body) = await Post("{\"a\":1}", null);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Equal("{\"Type\":\"generic\",\"ErrorMessage\":\"Unknown request type: Newtonsoft.Json.Linq.JObject (not a command, query or event)\"}", body);
        }

        [Fact]
        public async Task Request_without_handler_is_bad_request()
        {
            var (status, _, body) = await Post("{\"Data\":\"x\"}", TypeName<SampleCommand>());

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Equal("{\"Type\":\"generic\",\"ErrorMessage\":\"No handler registered for SampleCommand\"}", body);
        }

        [Fact]
        public async Task Failed_requests_are_logged_by_log_behavior()
        {
            await Post("{\"Mode\":\"crash\"}", TypeName<HttpCommand>());

            var entry = _host.Log.Events.Single(e => e.MessageTemplate.Text == "Handling {commandType} failed");
            Assert.IsType<ApplicationException>(entry.Exception);
        }
    }
}
