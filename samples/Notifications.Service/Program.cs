using Autofac;
using Orders.Api;
using Orders.Service;
using Serilog;
using ServiceComponents.AspNet.Exceptions;
using ServiceComponents.AspNet.HealthCheck;
using ServiceComponents.AspNet.Wireup;

namespace Notifications.Service;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        var rabbit = RabbitSettings.Load(args, "Notifications:Rabbit");

        var builder = new ServiceComponentsHostBuilder()
            .UseDefault(new[] { typeof(OrderPlaced).Assembly }, new[] { typeof(OrderPlacedHandler).Assembly })
            .UseSerilog((context, log) => log
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console())
            .ConfigureApp((configuration, environment, app) => {
                app.UseMiddleware<ErrorHandlingMiddleware>();
                app.UseRouting();
            })
            .AddEndpoints()
            // Minimal endpoint listing what has been notified, handy for demos and end-to-end tests.
            .RegisterCallback((Microsoft.Extensions.Configuration.IConfiguration configuration, Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/notifications", (INotificationLog log) => log.All))
            .AddHealthCheck((configuration, checks) => {
                if (rabbit.Enabled) {
                    checks.AddConsumers("rabbit-consumers", new[] { ServiceComponentsHealthcheckExtensions.ReadinessTag });
                }
            })
            .AddPrometheusMetrics()
            .ConfigureContainer((context, container) => {
                container.RegisterType<InMemoryNotificationLog>().As<INotificationLog>().SingleInstance();
                container.RegisterType<JsonExceptionMapper>().As<IExceptionMapperService>().SingleInstance();
            })
            // Queries go to the Orders service over HTTP; everything else stays in-process.
            .AddHttpSender(configuration => new Uri(configuration["Notifications:OrdersUri"] ?? "http://localhost:5100/api/generic"), "http")
            .AddCommandRouter(command => "loopback")
            .AddQueryRouter(query => "http")
            .AddEventRouter(@event => "loopback");

        if (rabbit.Enabled) {
            // Binds the "notifications" queue to the exchange the Orders service publishes its events to.
            builder.AddRabbit(rabbit.ConnectionString, rabbit.ClientName, rabbit.Queue, rabbit.Exchange, "", rabbit.RetryIntervals);
        }

        return builder.CreateHostBuilder(args);
    }
}
