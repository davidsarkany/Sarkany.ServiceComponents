using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceComponents.AspNet;
using ServiceComponents.AspNet.HealthCheck;
using ServiceComponents.AspNet.Http;
using ServiceComponents.AspNet.Monitoring;
using ServiceComponents.AspNet.OpenApi;
using ServiceComponents.AspNet.Services;
using ServiceComponents.AspNet.SSO;
using ServiceComponents.AspNet.Wireup;
using ServiceComponents.Infrastructure.Rabbit;
using ServiceComponents.UnitTests.Fixtures;
using Xunit;

namespace ServiceComponents.UnitTests.AspNet
{
    [Collection("prometheus")]
    public class HostingTests
    {
        private static string TypeName<T>() => $"{typeof(T).FullName}, {typeof(T).Assembly.GetName().Name}";

        private static async Task<(HttpStatusCode status, string body)> Get(HttpTestHost host, string path)
        {
            var response = await host.CreateClient().GetAsync(path);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private static async Task<(HttpStatusCode status, string body)> Post(HttpTestHost host, string json, string domainType, params (string, string)[] headers)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/generic") { Content = new StringContent(json, Encoding.UTF8) };
            request.Headers.Add("domain-type", domainType);
            foreach (var (key, value) in headers) {
                request.Headers.Add(key, value);
            }

            var response = await host.CreateClient().SendAsync(request);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Health_endpoints_filter_by_tags()
        {
            await using var host = new HttpTestHost(builder => builder.AddHealthCheck((_, checks) => checks
                .AddCheck("ready-only", () => HealthCheckResult.Unhealthy(), new[] { ServiceComponentsHealthcheckExtensions.ReadinessTag })
                .AddCheck("untagged", () => HealthCheckResult.Healthy())));

            var ready = await Get(host, "/.well-known/ready");
            var live = await Get(host, "/.well-known/live");

            Assert.Equal((HttpStatusCode.ServiceUnavailable, "Unhealthy"), ready);
            Assert.Equal((HttpStatusCode.OK, "Healthy"), live);
            Assert.Equal(("Liveness", "Readiness"), (ServiceComponentsHealthcheckExtensions.LivenessTag, ServiceComponentsHealthcheckExtensions.ReadinessTag));
            Assert.Equal(("/.well-known/ready", "/.well-known/live", "Liveness", "Readiness"),
                (HealthCheckConstants.ReadinessPath, HealthCheckConstants.LivenessPath, HealthCheckConstants.LivenessTag, HealthCheckConstants.ReadinessTag));
        }

        [Fact]
        public async Task Health_endpoint_paths_are_configurable()
        {
            await using var host = new HttpTestHost(builder => builder.AddHealthCheck((_, _) => { }, "/ready", "/live"));

            Assert.Equal(HttpStatusCode.OK, (await Get(host, "/ready")).status);
            Assert.Equal(HttpStatusCode.OK, (await Get(host, "/live")).status);
        }

        [Fact]
        public async Task Metrics_endpoint_exposes_prometheus_text()
        {
            await using var host = new HttpTestHost(builder => builder.AddPrometheusMetrics());
            await Post(host, "{\"Mode\":\"ok\"}", TypeName<HttpCommand>());

            var (status, body) = await Get(host, "/metrics");

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Contains("generic_request_counter{namespace=\"ServiceComponents.UnitTests.Fixtures\",name=\"HttpCommand\",kind=\"command\"}", body);
        }

        [Fact]
        public async Task Authorization_behavior_rejects_unauthenticated_callers()
        {
            await using var host = SecuredHost();

            var (status, body) = await Post(host, "{}", TypeName<SecuredCommand>());

            Assert.Equal(HttpStatusCode.Unauthorized, status);
            Assert.Equal("{\"Type\":\"security\",\"ErrorMessage\":\"RolesAuthorizationRequirement:User.IsInRole must be true for one of the following roles: (admin)\"}", body);
            Assert.Empty(host.Handled);
        }

        [Fact]
        public async Task Authorization_behavior_checks_roles()
        {
            await using var host = SecuredHost();

            var (status, body) = await Post(host, "{}", TypeName<SecuredCommand>(), ("x-roles", "user"));

            Assert.Equal(HttpStatusCode.Unauthorized, status);
            Assert.Contains("RolesAuthorizationRequirement", body);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(host, "{}", TypeName<SecuredQuery>(), ("x-roles", "user"))).status);
        }

        [Fact]
        public async Task Authorized_caller_keeps_http_correlation()
        {
            await using var host = SecuredHost();

            var (status, commandBody) = await Post(host, "{\"CommandId\":\"c-1\"}", TypeName<SecuredCommand>(), ("x-roles", "admin"), ("correlation-id", "corr"));
            var query = await Post(host, "{}", TypeName<SecuredQuery>(), ("x-roles", "admin"));

            Assert.True(HttpStatusCode.OK == status, commandBody);
            // String results go through MVC's plain-text formatter, not JSON (see Http_query_with_string_result_roundtrips_as_plain_text).
            Assert.Equal((HttpStatusCode.OK, "secret"), query);
            // Fixed (#4): the authorization decorator wraps the HTTP receiver chain, so the HTTP correlation behavior runs.
            Assert.Equal(("secured", "corr", (string)null, "c-1"), Assert.Single(host.Handled));
        }

        [Fact]
        public async Task Authorization_behavior_requires_a_handler()
        {
            await using var host = SecuredHost();

            var (status, body) = await Post(host, "{}", TypeName<SampleCommand>(), ("x-roles", "admin"));

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("No handler registered for SampleCommand", body);
        }

        private static HttpTestHost SecuredHost() => new HttpTestHost(builder => builder
            .RegisterCallback((_, services) => {
                services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, null);
                services.AddAuthorization();
            })
            .ConfigureApp((_, _, app) => {
                app.UseAuthentication();
                app.UseAuthorization();
            })
            .ConfigureContainer((_, container) => container.AddHttpReceiverAuthorizationBehavior()));

        [Fact]
        public async Task OpenApi_is_served()
        {
            await using var host = new HttpTestHost(builder => builder
                .AddOpenApi((_, options) => options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo { Title = "Test", Version = "v1" }),
                    (_, options) => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Test")));

            var (status, body) = await Get(host, "/swagger/v1/swagger.json");

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Contains("/api/Generic", body);
            Assert.Equal(HttpStatusCode.OK, (await Get(host, "/swagger/index.html")).status);
        }

        [Fact]
        public async Task Endpoint_route_builder_callbacks_are_invoked()
        {
            await using var host = new HttpTestHost(builder => builder.RegisterCallback((Microsoft.Extensions.Configuration.IConfiguration _, Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/custom", () => "custom")));

            Assert.Equal((HttpStatusCode.OK, "custom"), await Get(host, "/custom"));
        }

        [Fact]
        public async Task Startup_callbacks_are_invoked_with_the_root_scope()
        {
            ILifetimeScope received = null;
            await using var host = new HttpTestHost(builder => builder.RegisterCallback((Microsoft.Extensions.Configuration.IConfiguration _, ILifetimeScope scope) => received = scope));

            // Fixed (#19): StartupCallbacks run once the container is built.
            Assert.NotNull(received);
            Assert.NotNull(received.Resolve<Microsoft.Extensions.Configuration.IConfiguration>());
        }

        [Fact]
        public void Host_builder_collects_callbacks()
        {
            var builder = new ServiceComponentsHostBuilder()
                .RegisterCallback((MvcOptions _) => { })
                .RegisterCallback((IMvcBuilder _) => { })
                .AddGenericController("ignored");

            Assert.Single(builder.MvcOptionsBuilderCallbacks);
            Assert.Single(builder.MvcBuilderCallbacks);
            Assert.Empty(builder.ContainerBuilderCallbacks);
        }

        [Fact]
        public async Task Request_binder_deserializes_requests_with_newtonsoft()
        {
            var context = BindingContext(typeof(HttpCommand), "{\"Mode\":\"bound\",\"CommandId\":\"c-1\"}");

            await new RequestBinder().BindModelAsync(context);

            Assert.True(context.Result.IsModelSet);
            var command = Assert.IsType<HttpCommand>(context.Result.Model);
            Assert.Equal(("bound", "c-1"), (command.Mode, command.CommandId));
        }

        [Fact]
        public async Task Request_binder_fails_for_non_request_types()
        {
            var context = BindingContext(typeof(string), "\"text\"");

            await new RequestBinder().BindModelAsync(context);

            // Fixed: the Failed() result is no longer overwritten by Success.
            Assert.False(context.Result.IsModelSet);
        }

        [Fact]
        public void Request_binder_provider_and_helpers()
        {
            Assert.True(typeof(HttpCommand).IsRequest());
            Assert.True(typeof(HttpQuery).IsRequest());
            Assert.True(typeof(HttpEvent).IsRequest());
            Assert.False(typeof(string).IsRequest());

            var options = new MvcOptions().UseRequestBinder();
            Assert.IsType<RequestBinderProvider>(options.ModelBinderProviders[0]);
            Assert.Throws<ArgumentNullException>(() => new RequestBinderProvider().GetBinder(null));
        }

        private static ModelBindingContext BindingContext(Type modelType, string body)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            var metadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType);
            return DefaultModelBindingContext.CreateBindingContext(
                new ActionContext { HttpContext = httpContext },
                Mock.Of<IValueProvider>(), metadata, new BindingInfo(), "model");
        }

        [Fact]
        public async Task Background_queue_runs_work_items_and_survives_failures()
        {
            var queue = new BackgroundTaskQueue(10);
            var service = new QueuedHostedService(queue, NullLogger<QueuedHostedService>.Instance);
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await service.StartAsync(CancellationToken.None);
            await queue.QueueBackgroundWorkItemAsync(_ => throw new InvalidOperationException("fails"));
            await queue.QueueBackgroundWorkItemAsync(_ => {
                done.SetResult(true);
                return ValueTask.CompletedTask;
            });

            Assert.True(await done.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Same(queue, service.TaskQueue);
            await service.StopAsync(CancellationToken.None);
            await Assert.ThrowsAsync<ArgumentNullException>(() => queue.QueueBackgroundWorkItemAsync(null).AsTask());
        }

        [Fact]
        public async Task Background_queue_drops_oldest_when_full()
        {
            var queue = new BackgroundTaskQueue(1);
            var executed = new List<string>();

            await queue.QueueBackgroundWorkItemAsync(_ => { executed.Add("first"); return ValueTask.CompletedTask; });
            await queue.QueueBackgroundWorkItemAsync(_ => { executed.Add("second"); return ValueTask.CompletedTask; });
            await (await queue.DequeueAsync(CancellationToken.None))(CancellationToken.None);

            Assert.Equal(new[] { "second" }, executed);
        }

        [Fact]
        public async Task Consumer_health_check_reports_each_consumer()
        {
            var live = Mock.Of<IRabbitConsumer>(c => c.ConsumerTag == "a" && c.IsLive);
            var dead = Mock.Of<IRabbitConsumer>(c => c.ConsumerTag == "b" && !c.IsLive);

            var healthy = await new RabbitMQConsumerChecker(new[] { live }).CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
            var unhealthy = await new RabbitMQConsumerChecker(new[] { live, dead }).CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

            Assert.Equal((HealthStatus.Healthy, "All consumers are healthy"), (healthy.Status, healthy.Description));
            Assert.Equal((HealthStatus.Unhealthy, "One or more consumer is not healthy"), (unhealthy.Status, unhealthy.Description));
            Assert.Equal(new Dictionary<string, object> { ["a"] = "Healthy", ["b"] = "Unhealthy" }, unhealthy.Data);

            var services = new ServiceCollection();
            services.AddHealthChecks().AddConsumers("consumers", new[] { "Readiness" });
            var registration = services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>().Value.Registrations.Single();
            Assert.Equal("consumers", registration.Name);
            Assert.Equal(new[] { "Readiness" }, registration.Tags);
        }

        [Fact]
        public void Option_defaults()
        {
            var openApi = new OpenApiOptions();
            var document = new OpenApiDocumentOptions();
            var sso = new OpenApiSsoOptions();
            var jwt = new JwtTokenValidationOptions();

            Assert.Equal(("Document", "/swagger", "APP", "ClientId"), (openApi.DocumentName, openApi.Path, openApi.AppName, openApi.ClientId));
            Assert.Equal(("v0.1", "Title", "0.0.1"), (document.DocumentName, document.AppTitle, document.AppVersion));
            Assert.Equal(("OAuth2", "http", NSwag.OpenApiSecuritySchemeType.OAuth2, NSwag.OpenApiOAuth2Flow.Implicit, "http://localhost"),
                (sso.SecuritySchemeName, sso.SecurityScheme, sso.SecuritySchemeType, sso.OAuthFlow, sso.Authority));
            Assert.Equal(((string)null, "account", true, true, false), (jwt.Authority, jwt.Audience, jwt.RequireHttpsMetadata, jwt.SaveToken, jwt.ShowPII));
        }

        [Fact]
        public void Action_hiding_convention_hides_matching_actions()
        {
            var method = typeof(HostingTests).GetMethod(nameof(Option_defaults));
            var hidden = new Microsoft.AspNetCore.Mvc.ApplicationModels.ActionModel(method, Array.Empty<object>());
            var visible = new Microsoft.AspNetCore.Mvc.ApplicationModels.ActionModel(method, Array.Empty<object>()) { ActionName = "keep" };
            hidden.ActionName = "hide";
            var convention = new ActionHidingConvention(new List<Func<Microsoft.AspNetCore.Mvc.ApplicationModels.ActionModel, bool>> { a => a.ActionName == "hide" });

            convention.Apply(hidden);
            convention.Apply(visible);

            Assert.False(hidden.ApiExplorer.IsVisible);
            Assert.Null(visible.ApiExplorer.IsVisible);
        }
    }
}
