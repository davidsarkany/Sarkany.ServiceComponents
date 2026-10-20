using System.Reflection;
using Autofac;
using Microsoft.Extensions.Options;
using Serilog;
using ILogger = Serilog.ILogger;
using ServiceComponents.Core.Services;
using ServiceComponents.Infrastructure.Behaviors.Logging;
using ServiceComponents.Infrastructure.Behaviors.Stopwatch;
using ServiceComponents.Infrastructure.CorrelationContext;
using ServiceComponents.Infrastructure.Mediator;
using ServiceComponents.Infrastructure.Monitoring;
using ServiceComponents.Infrastructure.Receivers;
using ServiceComponents.Infrastructure.Validation;

namespace ServiceComponents.Benchmarks;

public enum Pipeline
{
    /// <summary>Receivers and dispatchers only.</summary>
    Bare,
    /// <summary>What UseDefault + AddPrometheusMetrics registers: hooks, validation, logging, stopwatch, correlation, metrics.</summary>
    Full
}

public static class Containers
{
    private static readonly Assembly Assembly = typeof(Containers).Assembly;

    /// <summary>Production-like logger: Information level, lower events are discarded, no sink I/O.</summary>
    public static ILogger Logger { get; } = new LoggerConfiguration().MinimumLevel.Information().Enrich.FromLogContext().CreateLogger();

    public static ContainerBuilder Mediator(Pipeline pipeline)
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(Logger).As<ILogger>();
        builder.RegisterType<ComputerClock>().AsImplementedInterfaces().SingleInstance();
        builder.RegisterInstance(Options.Create(new CorrelationLogOptions()));
        builder.AddMediator(Assembly);
        builder.AddCorrelationInfo();
        builder.AddReceivers();

        if (pipeline == Pipeline.Full) {
            builder.AddMediatorBehavior(Assembly);
            builder.AddValidationBehavior(Assembly);
            builder.AddLogBehavior();
            builder.AddStopwatchBehavior();
            builder.AddReceiverCorrelationLogEnricherBehavior();
            builder.AddPrometheusRequestMetricsService();
            builder.AddPrometheusRequestMetricsBehavior();
        }

        return builder;
    }
}
