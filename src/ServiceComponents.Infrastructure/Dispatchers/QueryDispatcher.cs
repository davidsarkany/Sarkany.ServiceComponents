using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Core.Extensions;

namespace ServiceComponents.Infrastructure.Dispatchers
{
    /// <summary>
    /// Dispatches a query to the single <c>IHandleQuery&lt;TQuery, TResult&gt;</c> registered for its runtime type.
    /// </summary>
    public class QueryDispatcher : IDispatchQuery
    {
        private readonly ILogger _log;
        private readonly ILifetimeScope _scope;

        public QueryDispatcher(ILogger log, ILifetimeScope scope)
        {
            _log = log;
            _scope = scope;
        }

        public async Task<TResult> DispatchAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            var invoker = QueryInvoker<TResult>.For(query);
            var handler = invoker.ResolveHandler(_scope) ?? throw new InvalidOperationException($"No handler found for {query.DisplayName()}");
            _log.Verbose("Dispatching {eventType} to {handlerType}", query.DisplayName(), handler.DisplayName());
            return await invoker.HandleAsync(handler, query, cancellationToken);
        }
    }
}
