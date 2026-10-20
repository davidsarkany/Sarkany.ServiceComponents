using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Core.Extensions;
using ServiceComponents.Infrastructure.CorrelationContext;

namespace ServiceComponents.Infrastructure.Dispatchers
{
    /// <summary>
    /// Dispatches an event to every <c>IHandleEvent&lt;T&gt;</c> registered for its runtime type. The handlers are
    /// resolved from a child lifetime scope that inherits the correlation of the dispatching scope, and run concurrently.
    /// </summary>
    public class EventDispatcher : IDispatchEvent
    {
        private readonly ILogger _log;
        private readonly ILifetimeScope _scope;

        public EventDispatcher(ILogger log, ILifetimeScope scope)
        {
            _log = log;
            _scope = scope;
        }

        public async Task DispatchAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent
        {
            await using var scope = _scope.BeginLifetimeScope();
            InheritCorrelation(scope);

            var invoker = EventInvoker.For(@event);
            var tasks = new List<Task>();
            foreach (var handler in invoker.ResolveHandlers(scope)) {
                _log.Verbose("Dispatching {eventType} to {handlerType}", @event.DisplayName(), handler.DisplayName());
                tasks.Add(invoker.HandleAsync(handler, @event, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        private void InheritCorrelation(ILifetimeScope scope)
        {
            if (!_scope.TryResolve<Correlation>(out var parent)
                || parent.CorrelationId == null && parent.CausationId == null && parent.CurrentId == null
                || !scope.TryResolve<Correlation>(out var child)
                || ReferenceEquals(parent, child)) {
                return;
            }

            child.CorrelationId = parent.CorrelationId;
            child.CausationId = parent.CausationId;
            child.CurrentId = parent.CurrentId;
        }
    }
}
