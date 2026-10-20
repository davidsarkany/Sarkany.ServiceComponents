using System.Collections.Concurrent;
using System.Text;
using Autofac;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Newtonsoft.Json;
using Orders.Api;
using Serilog;
using ILogger = Serilog.ILogger;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Mediator;
using ServiceComponents.Application.Senders;
using ServiceComponents.AspNet.Exceptions;
using ServiceComponents.AspNet.Wireup;
using Testcontainers.RabbitMq;

namespace ServiceComponents.LoadTests;

/// <summary>Command sent through RabbitMQ; its handler completes the waiting sender.</summary>
public class PingCommand : Command
{
    public PingCommand(string commandId = default) : base(commandId) { }
}

public class PingQuery : Query<string>
{
    public PingQuery(string queryId = default) : base(queryId) { }
}

public class PingHandlers : IHandleCommand<PingCommand>, IHandleQuery<PingQuery, string>
{
    public static readonly ConcurrentDictionary<string, TaskCompletionSource> Pending = new();

    public Task HandleAsync(PingCommand command, CancellationToken cancellationToken = default)
    {
        if (Pending.TryRemove(command.CommandId, out var completion)) {
            completion.TrySetResult();
        }

        return Task.CompletedTask;
    }

    public Task<string> HandleAsync(PingQuery query, CancellationToken cancellationToken = default) => Task.FromResult("pong");
}

public static class Scenarios
{
    private static readonly ILogger QuietLogger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();

    private static string TypeName(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";

    /// <summary>The Orders sample on Kestrel (loopback commands and queries, in-process events).</summary>
    public static async Task<List<ScenarioResult>> Http(ScenarioSettings settings)
    {
        using var host = Orders.Service.Program.CreateHostBuilder(new[] { "--urls", "http://127.0.0.1:0", "--Serilog:MinimumLevel:Default", "Warning" }).Build();
        await host.StartAsync();
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var client = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 1000 }) { BaseAddress = new Uri(address) };
        var run = Guid.NewGuid().ToString("N")[..8];

        async Task Post(object message)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/generic") {
                Content = new StringContent(JsonConvert.SerializeObject(message), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("domain-type", TypeName(message.GetType()));
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            await response.Content.ReadAsStringAsync();
        }

        var results = new List<ScenarioResult> {
            await LoadDriver.Run("http-command (PlaceOrder)", settings, i => Post(new PlaceOrder($"{run}-{i}", "customer", "product", 1))),
            await LoadDriver.Run("http-query (GetOrder)", settings, i => Post(new GetOrder($"{run}-{i % 1000 + 1}")))
        };
        await host.StopAsync();
        return results;
    }

    /// <summary>
    /// Command round trip through a real broker (send, consume, full pipeline, handle) and query RPC.
    /// The queue is named after the exchange so that Rabbit query RPC works with AddRabbit.
    /// </summary>
    public static async Task<List<ScenarioResult>> Rabbit(ScenarioSettings settings)
    {
        await using var broker = new RabbitMqBuilder().WithImage("rabbitmq:4.1-management").Build();
        await broker.StartAsync();

        using var host = new ServiceComponentsHostBuilder()
            .UseDefault(new[] { typeof(PingCommand).Assembly }, new[] { typeof(PingCommand).Assembly })
            .RegisterCallback(hostBuilder => hostBuilder.UseSerilog(QuietLogger))
            .ConfigureApp((_, _, app) => app.UseRouting())
            .AddEndpoints()
            .ConfigureContainer((_, container) => container.RegisterType<JsonExceptionMapper>().As<IExceptionMapperService>())
            .AddRabbit(broker.GetConnectionString(), "load", "load", "load", "", null)
            .AddCommandRouter(_ => "rabbit")
            .AddQueryRouter(_ => "rabbit")
            .AddEventRouter(_ => "loopback")
            .CreateHostBuilder(new[] { "--urls", "http://127.0.0.1:0" })
            .Build();
        await host.StartAsync();
        var root = host.Services.GetRequiredService<ILifetimeScope>();

        async Task SendCommand(long _)
        {
            var command = new PingCommand();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            PingHandlers.Pending[command.CommandId] = completion;
            await using var scope = root.BeginLifetimeScope();
            await scope.Resolve<ISendCommand>().SendAsync(command);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        async Task SendQuery(long _)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var scope = root.BeginLifetimeScope();
            await scope.Resolve<ISendQuery>().SendAsync(new PingQuery(), timeout.Token);
        }

        var results = new List<ScenarioResult> {
            await LoadDriver.Run("rabbit-command round trip", settings, SendCommand),
            await LoadDriver.Run("rabbit-query RPC", settings with { Concurrency = Math.Min(settings.Concurrency, 8) }, SendQuery)
        };
        await host.StopAsync();
        return results;
    }
}
