using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Prometheus;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Mediator;
using ServiceComponents.Application.Monitoring;
using ServiceComponents.Infrastructure.Mediator;
using ServiceComponents.Infrastructure.Monitoring;
using ServiceComponents.Infrastructure.Receivers;
using Xunit;

namespace ServiceComponents.UnitTests.Infrastructure
{
    // Prometheus metrics live in the process-wide default registry; this collection keeps the scraping tests serial.
    [CollectionDefinition("prometheus", DisableParallelization = true)]
    public class PrometheusCollection { }

    [Collection("prometheus")]
    public class MonitoringTests
    {
        public class MetricsProbeCommand : Command
        {
            public bool Fail { get; }
            public MetricsProbeCommand(bool fail) => Fail = fail;
        }

        public class MetricsProbeQuery : Query<int>
        {
            public bool Fail { get; }
            public MetricsProbeQuery(bool fail) : base(default) => Fail = fail;
        }

        public class MetricsProbeEvent : Event
        {
            public bool Fail { get; }
            public MetricsProbeEvent(bool fail) : base(default) => Fail = fail;
        }

        public class ProbeHandlers : IHandleCommand<MetricsProbeCommand>, IHandleQuery<MetricsProbeQuery, int>, IHandleEvent<MetricsProbeEvent>
        {
            public Task HandleAsync(MetricsProbeCommand command, CancellationToken cancellationToken = default) =>
                command.Fail ? throw new TimeoutException() : Task.CompletedTask;

            public Task<int> HandleAsync(MetricsProbeQuery query, CancellationToken cancellationToken = default) =>
                query.Fail ? throw new TimeoutException() : Task.FromResult(42);

            public Task HandleAsync(MetricsProbeEvent @event, CancellationToken cancellationToken = default) =>
                @event.Fail ? throw new TimeoutException() : Task.CompletedTask;
        }

        [CounterMetric("sc_test_custom_counter", "Custom counter")]
        public class CustomCounter
        {
            [MetricField("label_a")]
            public string A { get; } = "a";
            public string B { get; } = "b";
        }

        [GaugeMetric("sc_test_custom_gauge", "Custom gauge")]
        public class CustomGauge
        {
            public string Shard { get; } = "1";
        }

        [SummaryMetric("sc_test_custom_summary", "Custom summary")]
        public class CustomSummary
        {
            public string Op { get; } = "x";
        }

        [LinearHistogramMetric("sc_test_linear_histogram", "Linear histogram", 10, 10, 3)]
        public class LinearHistogram { }

        [ExponentialHistogramMetric("sc_test_exponential_histogram", "Exponential histogram", 1, 10, 3)]
        public class ExponentialHistogram { }

        public class Unannotated { }

        private static async Task<string> Scrape()
        {
            using var stream = new MemoryStream();
            await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(stream);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static double Value(string scrape, string series) =>
            double.Parse(scrape.Split('\n').Single(line => line.StartsWith(series + " ")).Substring(series.Length + 1), System.Globalization.CultureInfo.InvariantCulture);

        private const string Ns = "ServiceComponents.UnitTests.Infrastructure";

        [Fact]
        public async Task Request_metrics_have_stable_names_help_labels_and_semantics()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.AddMediator();
            builder.RegisterType<ProbeHandlers>().AsImplementedInterfaces();
            builder.AddReceivers();
            builder.AddPrometheusRequestMetricsService();
            builder.AddPrometheusRequestMetricsBehavior();
            await using var container = builder.Build();

            await container.Resolve<IReceiveCommand>().ReceiveAsync(new MetricsProbeCommand(false));
            await Assert.ThrowsAsync<TimeoutException>(() => container.Resolve<IReceiveCommand>().ReceiveAsync(new MetricsProbeCommand(true)));
            Assert.Equal(42, await container.Resolve<IReceiveQuery>().ReceiveAsync(new MetricsProbeQuery(false)));
            await Assert.ThrowsAsync<TimeoutException>(() => container.Resolve<IReceiveQuery>().ReceiveAsync(new MetricsProbeQuery(true)));
            await container.Resolve<IReceiveEvent>().ReceiveAsync(new MetricsProbeEvent(false));
            await Assert.ThrowsAsync<TimeoutException>(() => container.Resolve<IReceiveEvent>().ReceiveAsync(new MetricsProbeEvent(true)));

            var scrape = await Scrape();

            Assert.Contains("# HELP generic_request_counter Request counter", scrape);
            Assert.Contains("# TYPE generic_request_counter counter", scrape);
            Assert.Contains("# HELP request_failure_counter Request failure counter", scrape);
            Assert.Contains("# TYPE request_failure_counter counter", scrape);
            Assert.Contains("# HELP request_duration Request duration", scrape);
            Assert.Contains("# TYPE request_duration summary", scrape);
            Assert.Contains("# HELP generic_request_gauge Actual request counter", scrape);
            Assert.Contains("# TYPE generic_request_gauge gauge", scrape);

            foreach (var (kind, name) in new[] { ("command", "MetricsProbeCommand"), ("query", "MetricsProbeQuery"), ("event", "MetricsProbeEvent") }) {
                var labels = $"namespace=\"{Ns}\",name=\"{name}\",kind=\"{kind}\"";
                Assert.Equal(2, Value(scrape, $"generic_request_counter{{{labels}}}"));
                Assert.Equal(1, Value(scrape, $"request_failure_counter{{exception_name=\"TimeoutException\",{labels}}}"));
                // Command durations are recorded for failures too (using block); query and event durations only on success.
                Assert.Equal(kind == "command" ? 2 : 1, Value(scrape, $"request_duration_count{{{labels}}}"));
            }

            // The gauge is labelled by the public properties of the attribute it derives from.
            Assert.Equal(0, Value(scrape,
                "generic_request_gauge{Name=\"generic_request_gauge\",Title=\"Current request count\",TypeId=\"ServiceComponents.Infrastructure.Monitoring.RequestGaugeMetric\"}"));
        }

        [Fact]
        public async Task Metrics_service_supports_custom_metric_kinds()
        {
            var service = new PrometheusMetricsService();

            service.Increment(new CustomCounter(), 3);
            service.Increment(new CustomGauge(), 5);
            service.Decrement(new CustomGauge(), 2);
            service.Set(new CustomGauge(), 7);
            service.Observe(new CustomSummary(), 12);
            service.Observe(new LinearHistogram(), 15);
            service.Observe(new ExponentialHistogram(), 5);

            var scrape = await Scrape();

            Assert.Equal(3, Value(scrape, "sc_test_custom_counter{label_a=\"a\",B=\"b\"}"));
            Assert.Equal(7, Value(scrape, "sc_test_custom_gauge{Shard=\"1\"}"));
            Assert.Equal(1, Value(scrape, "sc_test_custom_summary_count{Op=\"x\"}"));
            Assert.Equal(1, Value(scrape, "sc_test_linear_histogram_bucket{le=\"20\"}"));
            Assert.Equal(0, Value(scrape, "sc_test_linear_histogram_bucket{le=\"10\"}"));
            Assert.Equal(1, Value(scrape, "sc_test_exponential_histogram_bucket{le=\"10\"}"));
        }

        [Fact]
        public void Metrics_service_rejects_wrong_metric_kinds()
        {
            var service = new PrometheusMetricsService();

            Assert.Throws<InvalidOperationException>(() => service.Increment(new CustomSummary()));
            Assert.Throws<InvalidOperationException>(() => service.Decrement(new CustomCounter()));
            Assert.Throws<InvalidOperationException>(() => service.Set(new CustomCounter(), 1));
            Assert.Throws<InvalidOperationException>(() => service.Observe(new CustomCounter(), 1));
            Assert.Throws<InvalidOperationException>(() => service.Observe(new CustomSummary(), 1, 1));
        }

        [Fact]
        public void Unannotated_metric_is_rejected_with_a_clear_error()
        {
            // Fixed (known issue #11): the error message used to be built from a null attribute (NullReferenceException).
            var error = Assert.Throws<InvalidOperationException>(() => new PrometheusMetricsService().Increment(new Unannotated()));

            Assert.Equal("Increment cannot be used on metric annotated as (no metric attribute)", error.Message);
        }

        [Fact]
        public async Task Histogram_count_observe_works_with_properties_and_exponential_buckets()
        {
            // Fixed (known issue #11): the (val, count) overload used labels on histograms created without label
            // names (exception), and created exponential histograms with linear buckets.
            var service = new PrometheusMetricsService();

            service.Observe(new CountLinearHistogram(), 1, 2);
            service.Observe(new LabelledLinearHistogram(), 1, 2);
            service.Observe(new CountExponentialHistogram(), 3, 1);

            var scrape = await Scrape();
            Assert.Equal(2, Value(scrape, "sc_test_count_linear_histogram_count"));
            Assert.Equal(2, Value(scrape, "sc_test_labelled_linear_histogram_count"));
            Assert.Equal(1, Value(scrape, "sc_test_count_exponential_histogram_bucket{le=\"4\"}"));
            Assert.Equal(0, Value(scrape, "sc_test_count_exponential_histogram_bucket{le=\"2\"}"));
        }

        [ExponentialHistogramMetric("sc_test_count_exponential_histogram", "Count exponential", 1, 2, 4)]
        public class CountExponentialHistogram { }

        [LinearHistogramMetric("sc_test_count_linear_histogram", "Count", 1, 1, 2)]
        public class CountLinearHistogram { }

        [LinearHistogramMetric("sc_test_labelled_linear_histogram", "Labelled", 1, 1, 2)]
        public class LabelledLinearHistogram
        {
            public string Label { get; } = "l";
        }

        [Fact]
        public void Metric_reflection_helpers()
        {
            Assert.Equal(new[] { "label_a", "B" }, new CustomCounter().MetricFields());
            Assert.Equal(new[] { "a", "b" }, new CustomCounter().MetricValues());
            Assert.Equal(("sc_test_custom_counter", "Custom counter"), new CustomCounter().MetricDescription());
            Assert.Equal(("Unannotated", Ns), new Unannotated().MetricDescription());
            Assert.Equal(new[] { "exception_name", "namespace", "name", "kind" },
                new RequestFailureMetric(new SampleCommand("x"), new Exception()).MetricFields());
            Assert.Equal(new[] { "Name", "Title", "TypeId" }, new RequestGaugeMetric("n", "t").MetricFields());
        }

        [Fact]
        public void Request_metrics_for_types_without_namespace_use_empty_label()
        {
            // Fixed (known issue #11): a null namespace used to fail the request with NullReferenceException.
            Assert.Equal(new[] { "", "GlobalNamespaceCommand", "command" }, new RequestCounterMetric(new GlobalNamespaceCommand()).MetricValues());
        }

        [Fact]
        public void Request_metrics_kind_and_names()
        {
            var query = new RequestDurationMetric(new SampleQuery(1));
            var @event = new RequestCounterMetric(new SampleEvent("e"));
            var failure = new RequestFailureMetric(new SampleQuery(1), new ArgumentException());
            var eventFailure = new RequestFailureMetric(new SampleEvent("e"), new ArgumentException());

            Assert.Equal(("ServiceComponents.UnitTests", "SampleQuery", "query"), (query.NameSpace, query.Name, query.Kind));
            Assert.Equal("event", @event.Kind);
            Assert.Equal("ArgumentException", failure.ExceptionName);
            Assert.Equal("event", eventFailure.Kind);
            Assert.Equal("command", new RequestDurationMetric(new SampleCommand("c")).Kind);
            Assert.Equal("event", new RequestDurationMetric(new SampleEvent("c")).Kind);
            Assert.Equal("query", new RequestCounterMetric(new SampleQuery(1)).Kind);
        }

        [Fact]
        public void Tracing_service_can_be_constructed()
        {
            Assert.NotNull(new TracingService(OpenTracing.Noop.NoopTracerFactory.Create()));
        }
    }
}
