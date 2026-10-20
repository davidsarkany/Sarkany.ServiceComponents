using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application;
using ServiceComponents.Infrastructure.Receivers.Loopback;

namespace ServiceComponents.Infrastructure.Senders.Loopback
{
    public class LoopbackEventPublisher : IPublishLoopbackEvent
    {
        private readonly ILifetimeScope _scope;

        public LoopbackEventPublisher(ILifetimeScope scope)
        {
            _scope = scope;
        }

        public async Task PublishAsync<TEvent>(TEvent @event, ICorrelation correlation, CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            await using var scope = _scope.BeginLifetimeScope();
            await scope.Resolve<IReceiveLoopbackEvent>().ReceiveAsync(Unwrap(@event), correlation, cancellationToken);
        }

        // A routing key only matters to RabbitMQ; in-process the wrapped event is delivered.
        private static IEvent Unwrap(IEvent @event) => @event is RoutableEvent routable ? routable.Event : @event;

        public async Task PublishAsync(IEnumerable<IEvent> events, ICorrelation correlation, CancellationToken cancellationToken = default)
        {
            await using var scope = _scope.BeginLifetimeScope();
            var receiver = scope.Resolve<IReceiveLoopbackEvent>();

            foreach (IEvent @event in events) {
                await receiver.ReceiveAsync(Unwrap(@event), correlation, cancellationToken);
            }
        }
    }
}