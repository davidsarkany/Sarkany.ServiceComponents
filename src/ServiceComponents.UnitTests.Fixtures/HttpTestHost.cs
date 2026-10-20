using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application;
using ServiceComponents.Application.Mediator;
using ServiceComponents.AspNet.Exceptions;
using ServiceComponents.AspNet.Http;
using ServiceComponents.AspNet.Monitoring;
using ServiceComponents.AspNet.Wireup;
using ServiceComponents.Core.Exceptions;


namespace ServiceComponents.UnitTests.Fixtures
{
    /// <summary>
    /// Real <see cref="ServiceComponentsHostBuilder"/> host (UseDefault wireup) running on TestServer.
    /// </summary>
    public sealed class HttpTestHost : IAsyncDisposable
    {
        public IHost Host { get; }
        public TestServer Server { get; }
        public ReceivedRequests Received { get; } = new ReceivedRequests();
        public TestLog Log { get; } = new TestLog();
        public RecordingExceptionMapper Errors { get; } = new RecordingExceptionMapper();

        public HttpTestHost(Action<ServiceComponentsHostBuilder> configure = null)
        {
            var builder = new ServiceComponentsHostBuilder()
                .UseDefault(new[] { typeof(HttpCommand).Assembly }, new[] { typeof(HttpCommand).Assembly })
                .UseSerilog((_, configuration) => configuration.MinimumLevel.Verbose().Enrich.FromLogContext().WriteTo.Sink(Log))
                .ConfigureApp((_, _, app) => {
                    app.UseMiddleware<ErrorHandlingMiddleware>();
                    app.UseRouting();
                })
                .ConfigureMvc(mvc => mvc
                    .AddApplicationPart(typeof(GenericController).Assembly)
                    .AddApplicationPart(typeof(MetricsController).Assembly)
                    .AddNewtonsoftJson())
                .RegisterCallback((_, services) => services.AddSingleton<IServer, TestServer>())
                .ConfigureContainer((_, container) => {
                    container.RegisterInstance(Received);
                    container.RegisterInstance(Errors).As<IExceptionMapperService>();
                });
            configure?.Invoke(builder);
            // Endpoints last, so middleware added by 'configure' (e.g. authentication) runs before them.
            builder.AddEndpoints();

            Host = builder.CreateHostBuilder(Array.Empty<string>()).Build();
            Host.Start();
            Server = (TestServer)Host.Services.GetRequiredService<IServer>();
        }

        public HttpClient CreateClient() => Server.CreateClient();

        public string[] Headers => Received.Items.Where(i => i.what.StartsWith("headers:")).Select(i => i.what.Substring(8)).ToArray();

        public (string what, string correlationId, string causationId, string currentId)[] Handled =>
            Received.Items.Where(i => !i.what.StartsWith("headers:")).ToArray();

        public async ValueTask DisposeAsync()
        {
            await Host.StopAsync();
            Host.Dispose();
        }
    }

    /// <summary>
    /// <see cref="JsonExceptionMapper"/> that remembers the exceptions it mapped (the middleware itself does not log them).
    /// </summary>
    public class RecordingExceptionMapper : IExceptionMapperService
    {
        private readonly JsonExceptionMapper _inner = new JsonExceptionMapper();
        public ConcurrentQueue<Exception> Exceptions { get; } = new ConcurrentQueue<Exception>();

        public Task WriteResponse(Exception exception, Microsoft.AspNetCore.Http.HttpResponse httpResponse)
        {
            Exceptions.Enqueue(exception);
            return _inner.WriteResponse(exception, httpResponse);
        }

        public Task ThrowExceptionIfNeeded(HttpResponseMessage httpResponse) => _inner.ThrowExceptionIfNeeded(httpResponse);
    }

    /// <summary>
    /// <see cref="IHttpClientFactory"/> handing out clients bound to a <see cref="TestServer"/>.
    /// </summary>
    public class TestServerHttpClientFactory : IHttpClientFactory
    {
        private readonly TestServer _server;
        public List<string> RequestedNames { get; } = new List<string>();

        public TestServerHttpClientFactory(TestServer server) => _server = server;

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            return _server.CreateClient();
        }
    }
}
