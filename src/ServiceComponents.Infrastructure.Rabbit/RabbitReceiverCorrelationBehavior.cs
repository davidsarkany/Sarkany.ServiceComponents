using System;
using System.Text;
using RabbitMQ.Client.Events;
using Serilog;
using Serilog.Events;
using ServiceComponents.Infrastructure.CorrelationContext;

namespace ServiceComponents.Infrastructure.Rabbit
{
    public abstract class RabbitReceiverCorrelationBehavior
    {
        private readonly ILogger _log;
        private readonly Correlation _correlation;

        protected RabbitReceiverCorrelationBehavior(ILogger log, Correlation correlation)
        {
            _log = log;
            _correlation = correlation;
        }

        protected void UpdateCorrelation(BasicDeliverEventArgs args, string currentId)
        {
            _correlation.CorrelationId = args.BasicProperties.IsCorrelationIdPresent() ? args.BasicProperties.CorrelationId : Guid.NewGuid().ToString();
            if (args.BasicProperties.Headers != null && args.BasicProperties.Headers.TryGetValue("causation_id", out var causationId)) {
                // Strings arrive as byte arrays; other clients may send the header as another AMQP type.
                _correlation.CausationId = causationId is byte[] bytes ? Encoding.UTF8.GetString(bytes) : causationId?.ToString();
            }

            _correlation.CurrentId = currentId;

            if (_log.IsEnabled(LogEventLevel.Debug)) {
                _log.ForContext("correlation", _correlation, true).Debug("Correlation updated from RabbitMQ basic properties");
            }
        }
    }
}