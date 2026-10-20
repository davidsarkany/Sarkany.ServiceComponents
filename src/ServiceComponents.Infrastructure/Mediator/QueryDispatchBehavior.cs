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
    /// Runs the query hooks around dispatching:
    /// pre → pre&lt;T&gt; → handler → post → post&lt;T&gt;, and on failure: failure → failure&lt;T&gt;, then rethrows.
    /// </summary>
    public class QueryDispatchBehavior : IDispatchQuery
    {
        private readonly ILifetimeScope _scope;
        private readonly IDispatchQuery _next;

        public QueryDispatchBehavior(ILifetimeScope scope, IDispatchQuery next)
        {
            _scope = scope;
            _next = next;
        }

        public async Task<TResult> DispatchAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            var invoker = QueryInvoker<TResult>.For(query);
            try {
                foreach (var hook in _scope.Resolve<IEnumerable<IPreHandleQuery>>()) {
                    await hook.PreHandleAsync(query, cancellationToken);
                }

                await invoker.PreHandleAsync(_scope, query, cancellationToken);

                var result = await _next.DispatchAsync(query, cancellationToken);

                foreach (var hook in _scope.Resolve<IEnumerable<IPostHandleQuery>>()) {
                    await hook.PostHandleAsync(query, result, cancellationToken);
                }

                await invoker.PostHandleAsync(_scope, query, result, cancellationToken);
                return result;
            }
            catch (Exception exception) {
                foreach (var hook in _scope.Resolve<IEnumerable<IHandleQueryFailure>>()) {
                    await hook.HandleFailureAsync(query, exception, cancellationToken);
                }

                await invoker.HandleFailureAsync(_scope, query, exception, cancellationToken);
                throw;
            }
        }
    }
}
