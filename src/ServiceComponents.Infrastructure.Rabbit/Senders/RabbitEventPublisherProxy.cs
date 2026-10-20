using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Senders;

namespace ServiceComponents.Infrastructure.Rabbit.Senders
{
    public class RabbitEventPublisherProxy : IPublishEvent
    {
        private readonly IChannel _channel;
        private readonly IPublishRabbitEvent _rabbitSender;

        private readonly IReadOnlyBasicProperties _template;

        public RabbitEventPublisherProxy(IChannel channel, IPublishRabbitEvent rabbitSender)
            : this(channel, rabbitSender, null)
        {
        }

        // The template is the basicProperties argument of AddRabbitEventPublisher (e.g. persistent delivery mode).
        internal RabbitEventPublisherProxy(IChannel channel, IPublishRabbitEvent rabbitSender, IReadOnlyBasicProperties template)
        {
            _channel = channel;
            _rabbitSender = rabbitSender;
            _template = template;
        }

        public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            var basicProperties = _template == null ? new BasicProperties() : new BasicProperties(_template);
            basicProperties.Headers = _template?.Headers == null ? new Dictionary<string, object>() : new Dictionary<string, object>(_template.Headers);

            await _rabbitSender.PublishAsync(@event, basicProperties, cancellationToken);
        }

        public async Task PublishAsync(IEnumerable<IEvent> events, CancellationToken cancellationToken = default)
        {
            foreach (var @event in events)
            {
                await PublishAsync(@event, cancellationToken);
            }
        }
    }
}
