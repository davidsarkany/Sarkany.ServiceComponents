using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServiceComponents.Infrastructure.Rabbit;

namespace ServiceComponents.AspNet.HealthCheck
{
    public class RabbitMQConsumerChecker : IHealthCheck
    {
        private readonly IEnumerable<IRabbitConsumer> _consumers;

        public RabbitMQConsumerChecker(IEnumerable<IRabbitConsumer> consumers)
        {
            _consumers = consumers;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            var data = new Dictionary<string, object>();
            var result = HealthCheckResult.Healthy("All consumers are healthy", data: data);
            foreach (var consumer in _consumers) {
                cancellationToken.ThrowIfCancellationRequested();
                // Tags may repeat (or be missing before the consumer started); an unhealthy entry is never overwritten.
                var tag = consumer.ConsumerTag ?? string.Empty;
                if (!data.TryGetValue(tag, out var existing) || (string)existing == "Healthy") {
                    data[tag] = consumer.IsLive ? "Healthy" : "Unhealthy";
                }
                if (!consumer.IsLive) {
                    result = HealthCheckResult.Unhealthy("One or more consumer is not healthy", data: data);
                }
            }
            return Task.FromResult(result);
        }
    }
}
