using System;
using System.Collections.Concurrent;
using Prometheus;
using ServiceComponents.Application.Monitoring;

namespace ServiceComponents.Infrastructure.Monitoring
{
    /// <summary>
    /// <see cref="IMetricsService"/> on the prometheus-net default registry. The metric is chosen by the attribute of the
    /// metric object's class (see <see cref="MetricExtensionMethods"/>); one Prometheus metric is created per class.
    /// Counters, gauges and summaries are labelled by the object's properties; histograms have no labels.
    /// </summary>
    public class PrometheusMetricsService : IMetricsService
    {
        private readonly ConcurrentDictionary<Type, Counter> _counters = new();
        private readonly ConcurrentDictionary<Type, Summary> _summaries = new();
        private readonly ConcurrentDictionary<Type, Histogram> _histograms = new();
        private readonly ConcurrentDictionary<Type, Gauge> _gauges = new();

        public void Increment(object metric, double increment = 1)
        {
            var descriptor = MetricDescriptor.For(metric.GetType());
            switch (descriptor.Kind) {
                case CounterMetric:
                    Counter(metric.GetType(), descriptor).WithLabels(descriptor.LabelValues(metric)).Inc(increment);
                    break;
                case GaugeMetric:
                    Gauge(metric.GetType(), descriptor).WithLabels(descriptor.LabelValues(metric)).Inc(increment);
                    break;
                default:
                    throw new InvalidOperationException($"Increment cannot be used on metric annotated as {descriptor.KindName}");
            }
        }

        public void Decrement(object metric, double decrement = 1)
        {
            var descriptor = MetricDescriptor.For(metric.GetType());
            if (descriptor.Kind is not GaugeMetric) {
                throw new InvalidOperationException($"Decrement cannot be used on metric annotated as {descriptor.KindName}");
            }

            Gauge(metric.GetType(), descriptor).WithLabels(descriptor.LabelValues(metric)).Dec(decrement);
        }

        public void Set(object metric, double target)
        {
            var descriptor = MetricDescriptor.For(metric.GetType());
            if (descriptor.Kind is not GaugeMetric) {
                throw new InvalidOperationException($"Set cannot be used on metric annotated as {descriptor.KindName}");
            }

            Gauge(metric.GetType(), descriptor).WithLabels(descriptor.LabelValues(metric)).Set(target);
        }

        public void Observe(object metric, double duration)
        {
            var descriptor = MetricDescriptor.For(metric.GetType());
            switch (descriptor.Kind) {
                case SummaryMetric:
                    _summaries.GetOrAdd(metric.GetType(), _ => Metrics.CreateSummary(descriptor.Name, descriptor.Title,
                            new SummaryConfiguration { LabelNames = descriptor.LabelNames }))
                        .WithLabels(descriptor.LabelValues(metric)).Observe(duration);
                    break;
                case HistogramMetric histogram:
                    Histogram(metric.GetType(), descriptor, histogram).Observe(duration);
                    break;
                default:
                    throw new InvalidOperationException($"Observe cannot be used on metric annotated as {descriptor.KindName}");
            }
        }

        public void Observe(object metric, double val, long count)
        {
            var descriptor = MetricDescriptor.For(metric.GetType());
            if (descriptor.Kind is not HistogramMetric histogram) {
                throw new InvalidOperationException($"Observe cannot be used on metric annotated as {descriptor.KindName}");
            }

            Histogram(metric.GetType(), descriptor, histogram).Observe(val, count);
        }

        private Counter Counter(Type type, MetricDescriptor descriptor) =>
            _counters.GetOrAdd(type, _ => Metrics.CreateCounter(descriptor.Name, descriptor.Title, new CounterConfiguration { LabelNames = descriptor.LabelNames }));

        private Gauge Gauge(Type type, MetricDescriptor descriptor) =>
            _gauges.GetOrAdd(type, _ => Metrics.CreateGauge(descriptor.Name, descriptor.Title, new GaugeConfiguration { LabelNames = descriptor.LabelNames }));

        private Histogram Histogram(Type type, MetricDescriptor descriptor, HistogramMetric histogram) =>
            _histograms.GetOrAdd(type, _ => Metrics.CreateHistogram(descriptor.Name, descriptor.Title, new HistogramConfiguration {
                Buckets = histogram switch {
                    LinearHistogramMetric linear => Prometheus.Histogram.LinearBuckets(linear.Start, linear.Width, linear.Count),
                    ExponentialHistogramMetric exponential => Prometheus.Histogram.ExponentialBuckets(exponential.Start, exponential.Factor, exponential.Count),
                    _ => null
                }
            }));
    }
}
