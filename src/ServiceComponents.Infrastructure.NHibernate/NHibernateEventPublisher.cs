using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NHibernate;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Senders;
using ServiceComponents.Core.Services;

namespace ServiceComponents.Infrastructure.NHibernate
{
    public class NHibernateEventPublisherBehavior : IPublishEvent, IDisposable, IAsyncDisposable
    {
        private readonly ISession _session;
        private readonly IClock _clock;
        private readonly IPublishEvent _next;
        private readonly IList<IEvent> _unpublishedEvents = new List<IEvent>();

        public NHibernateEventPublisherBehavior(ISession session, IClock clock, IPublishEvent next)
        {
            _session = session;
            _clock = clock;
            _next = next;
        }

        public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            await _session.SaveAsync(new UnpublishedEventEntity {
         
                TimeStamp = _clock.UtcNow,
                EventType = @event.GetType().ToString(),
                EventBody = JsonConvert.SerializeObject(@event, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All })

            }, cancellationToken);

            _unpublishedEvents.Add(@event);
        }

        public async Task PublishAsync(IEnumerable<IEvent> events, CancellationToken cancellationToken = default)
        {
            foreach (var @event in events) {
                await PublishAsync(@event, cancellationToken);
            }
        }

        public void Dispose()
        {
            // Synchronous disposal blocks; Autofac uses DisposeAsync when the scope is disposed asynchronously.
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            // Each event is published once, even when the behavior is disposed again.
            while (_unpublishedEvents.Count > 0) {
                var unpublishedEvent = _unpublishedEvents[0];
                _unpublishedEvents.RemoveAt(0);
                await _next.PublishAsync(unpublishedEvent, CancellationToken.None);
            }

            GC.SuppressFinalize(this);
        }
    }

    public class UnpublishedEventEntity
    {
        public virtual long Id { get; set; }
        public virtual DateTime TimeStamp { get; set; }
        public virtual string EventType { get; set; }
        public virtual string EventBody { get; set; }
    }
}
