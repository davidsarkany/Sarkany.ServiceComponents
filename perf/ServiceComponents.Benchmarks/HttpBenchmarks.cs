using System.Text;
using Autofac;
using Serilog;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Newtonsoft.Json;
using ServiceComponents.Application.Senders;
using ServiceComponents.AspNet.Exceptions;
using ServiceComponents.AspNet.Http;
using ServiceComponents.AspNet.Http.Senders;
using ServiceComponents.AspNet.Wireup;

namespace ServiceComponents.Benchmarks;

/// <summary>Generic endpoint as consumer applications implement it.</summary>
[Route("api/[controller]")]
[ApiController]
public class GenericController : ControllerBase
{
    private readonly HttpRequestParser _parser;
    private readonly IReceiveHttpCommand _commandReceiver;
    private readonly IReceiveHttpQuery _queryReceiver;
    private readonly IReceiveHttpEvent _eventReceiver;

    public GenericController(HttpRequestParser parser, IReceiveHttpCommand commandReceiver, IReceiveHttpQuery queryReceiver, IReceiveHttpEvent eventReceiver)
    {
        _parser = parser;
        _commandReceiver = commandReceiver;
        _queryReceiver = queryReceiver;
        _eventReceiver = eventReceiver;
    }

    [HttpPost]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        object result = null;
        await _parser.Parse(Request,
            async (command, ct) => await _commandReceiver.ReceiveAsync(command, ct),
            async (query, ct) => result = await _queryReceiver.ReceiveAsync((dynamic)query, ct),
            async (@event, ct) => await _eventReceiver.ReceiveAsync(@event, ct),
            cancellationToken);
        return result == null ? Ok() : Ok(result);
    }
}

public static class BenchHost
{
    /// <summary>UseDefault host (as ReferenceApplication2 / the samples) running on TestServer.</summary>
    public static IHost Create()
    {
        var host = new ServiceComponentsHostBuilder()
            .UseDefault(new[] { typeof(BenchHost).Assembly }, new[] { typeof(BenchHost).Assembly })
            .RegisterCallback(hostBuilder => hostBuilder.UseSerilog(Containers.Logger))
            .ConfigureApp((_, _, app) => {
                app.UseMiddleware<ErrorHandlingMiddleware>();
                app.UseRouting();
            })
            .AddEndpoints()
            .ConfigureMvc(mvc => mvc.AddApplicationPart(typeof(BenchHost).Assembly).AddNewtonsoftJson())
            .AddPrometheusMetrics()
            .RegisterCallback((_, services) => services.AddSingleton<IServer, TestServer>())
            .ConfigureContainer((_, container) => container.RegisterType<JsonExceptionMapper>().As<IExceptionMapperService>().SingleInstance())
            .CreateHostBuilder(Array.Empty<string>())
            .Build();
        host.Start();
        return host;
    }

    public static TestServer Server(this IHost host) => (TestServer)host.Services.GetRequiredService<IServer>();
}

/// <summary>HTTP receive path (MVC + parser + full pipeline) and HTTP send path, in memory.</summary>
[MemoryDiagnoser]
public class HttpBenchmarks
{
    private IHost _host;
    private HttpClient _client;
    private IContainer _sender;
    private string _commandJson;
    private string _queryJson;
    private readonly BenchCommand _command = new("name", 5);
    private readonly BenchQuery _query = new(7);

    private class ServerClientFactory : IHttpClientFactory
    {
        private readonly TestServer _server;
        public ServerClientFactory(TestServer server) => _server = server;
        public HttpClient CreateClient(string name) => _server.CreateClient();
    }

    [GlobalSetup]
    public void Setup()
    {
        _host = BenchHost.Create();
        _client = _host.Server().CreateClient();
        _commandJson = JsonConvert.SerializeObject(_command);
        _queryJson = JsonConvert.SerializeObject(_query);

        var builder = Containers.Mediator(Pipeline.Bare);
        builder.RegisterInstance(new ServerClientFactory(_host.Server())).As<IHttpClientFactory>();
        builder.RegisterInstance(Microsoft.Extensions.Options.Options.Create(new ServiceComponents.AspNet.Http.HttpRequestOptions()));
        builder.RegisterType<JsonExceptionMapper>().As<IExceptionMapperService>();
        builder.AddHttpCommandSender(new Uri("http://localhost/api/generic"));
        builder.AddHttpQuerySender(new Uri("http://localhost/api/generic"));
        builder.AddHttpSenderCorrelationBehavior();
        _sender = builder.Build();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _sender.Dispose();
        _client.Dispose();
        _host.StopAsync().GetAwaiter().GetResult();
        _host.Dispose();
    }

    private async Task<string> Post(string json, Type type)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/generic") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("domain-type", Payloads.TypeName(type));
        request.Headers.Add("correlation-id", "corr");
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [Benchmark(Description = "Receive command")]
    public Task<string> ReceiveCommand() => Post(_commandJson, typeof(BenchCommand));

    [Benchmark(Description = "Receive query")]
    public Task<string> ReceiveQuery() => Post(_queryJson, typeof(BenchQuery));

    [Benchmark(Description = "Send command (sender + receiver)")]
    public async Task SendCommand()
    {
        await using var scope = _sender.BeginLifetimeScope();
        await scope.Resolve<ISendCommand>().SendAsync(_command);
    }

    [Benchmark(Description = "Send query (sender + receiver)")]
    public async Task<BenchResult> SendQuery()
    {
        await using var scope = _sender.BeginLifetimeScope();
        return await scope.Resolve<ISendQuery>().SendAsync(_query);
    }
}

/// <summary>Cost of building and starting a UseDefault host (container build, assembly scanning, MVC setup).</summary>
[MemoryDiagnoser]
public class StartupBenchmarks
{
    [Benchmark(Description = "Build and start UseDefault host")]
    public async Task BuildHost()
    {
        using var host = BenchHost.Create();
        await host.StopAsync();
    }
}
