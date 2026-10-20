using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Mediator;
using ServiceComponents.Infrastructure.Dispatchers;

namespace ServiceComponents.Infrastructure.Mediator
{
    /// <summary>
    /// Runs the event hooks around dispatching:
    /// pre → pre&lt;T&gt; → handlers → post → post&lt;T&gt;, and on failure: failure → failure&lt;T&gt;, then rethrows.
    /// </summary>
    public class EventDispatchBehavior : IDispatchEvent
    {
        private readonly ILifetimeScope _scope;
        private readonly IDispatchEvent _next;

        public EventDispatchBehavior(ILifetimeScope scope, IDispatchEvent next)
        {
            _scope = scope;
            _next = next;
        }

        public async Task DispatchAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent
        {
            var invoker = EventInvoker.For(@event);
            try {
                foreach (var hook in _scope.Resolve<IEnumerable<IPreHandleEvent>>()) {
                    await hook.PreHandleAsync(@event, cancellationToken);
                }

                await invoker.PreHandleAsync(_scope, @event, cancellationToken);

                await _next.DispatchAsync(@event, cancellationToken);

                foreach (var hook in _scope.Resolve<IEnumerable<IPostHandleEvent>>()) {
                    await hook.PostHandleAsync(@event, cancellationToken);
                }

                await invoker.PostHandleAsync(_scope, @event, cancellationToken);
            }
            catch (Exception exception) {
                foreach (var hook in _scope.Resolve<IEnumerable<IHandleEventFailure>>()) {
                    await hook.HandleFailureAsync(@event, exception, cancellationToken);
                }

                await invoker.HandleFailureAsync(_scope, @event, exception, cancellationToken);
                throw;
            }
        }
    }
}
