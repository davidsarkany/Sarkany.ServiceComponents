using Autofac;
using Microsoft.OpenApi;
using Newtonsoft.Json.Converters;
using Orders.Api;
using Orders.Application;
using Serilog;
using ServiceComponents.AspNet.Exceptions;
using ServiceComponents.AspNet.HealthCheck;
using ServiceComponents.AspNet.Wireup;

namespace Orders.Service;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    // Public and named CreateHostBuilder so that WebApplicationFactory (ServiceComponents.Testing.TestHost) can find it.
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        var rabbit = RabbitSettings.Load(args, "Orders:Rabbit");

        var builder = new ServiceComponentsHostBuilder()
            // Autofac, MVC with the request binder, mediator with validation, logging, stopwatch and correlation
            // behaviors, HTTP + loopback receivers and a loopback sender registered with the key "loopback".
            .UseDefault(new[] { typeof(PlaceOrder).Assembly }, new[] { typeof(PlaceOrderHandler).Assembly })
            .UseSerilog((context, log) => log
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console())
            .ConfigureApp((configuration, environment, app) => {
                // Maps exceptions to the JSON error contract (see IExceptionMapperService below).
                app.UseMiddleware<ErrorHandlingMiddleware>();
                app.UseRouting();
            })
            .AddEndpoints()
            .ConfigureMvc(mvc => mvc
                .AddApplicationPart(typeof(Program).Assembly)
                .AddNewtonsoftJson(options => options.SerializerSettings.Converters.Add(new StringEnumConverter())))
            .AddOpenApi(
                (configuration, options) => options.SwaggerDoc("v1", new OpenApiInfo { Title = "Orders", Version = "v1" }),
                (configuration, options) => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Orders v1"))
            .AddHealthCheck((configuration, checks) => {
                if (rabbit.Enabled) {
                    checks.AddConsumers("rabbit-consumers", new[] { ServiceComponentsHealthcheckExtensions.ReadinessTag });
                }
            })
            .AddPrometheusMetrics()
            .ConfigureContainer((context, container) => {
                container.RegisterType<InMemoryOrderStore>().As<IOrderStore>().SingleInstance();
                container.RegisterType<JsonExceptionMapper>().As<IExceptionMapperService>().SingleInstance();
            })
            // Commands and queries are handled in-process; events go to RabbitMQ when it is enabled.
            .AddCommandRouter(command => "loopback")
            .AddQueryRouter(query => "loopback")
            .AddEventRouter(@event => rabbit.Enabled ? "rabbit" : "loopback");

        if (rabbit.Enabled) {
            // Registers connections, consumers, the "rabbit" senders and declares the topology at startup.
            builder.AddRabbit(rabbit.ConnectionString, rabbit.ClientName, rabbit.Queue, rabbit.Exchange, "", rabbit.RetryIntervals);
        }

        return builder.CreateHostBuilder(args);
    }
}
