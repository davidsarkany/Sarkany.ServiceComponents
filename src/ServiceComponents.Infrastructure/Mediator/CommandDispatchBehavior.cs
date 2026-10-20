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
    /// Runs the command hooks around dispatching:
    /// pre → pre&lt;T&gt; → handler → post&lt;T&gt; → post, and on failure: failure → failure&lt;T&gt;, then rethrows.
    /// </summary>
    public class CommandDispatchBehavior : IDispatchCommand
    {
        private readonly ILifetimeScope _scope;
        private readonly IDispatchCommand _next;

        public CommandDispatchBehavior(ILifetimeScope scope, IDispatchCommand next)
        {
            _scope = scope;
            _next = next;
        }

        public async Task DispatchAsync<T>(T command, CancellationToken cancellationToken = default) where T : ICommand
        {
            var invoker = CommandInvoker.For(command);
            try {
                foreach (var hook in _scope.Resolve<IEnumerable<IPreHandleCommand>>()) {
                    await hook.PreHandleAsync(command, cancellationToken);
                }

                await invoker.PreHandleAsync(_scope, command, cancellationToken);

                await _next.DispatchAsync(command, cancellationToken);

                await invoker.PostHandleAsync(_scope, command, cancellationToken);
                foreach (var hook in _scope.Resolve<IEnumerable<IPostHandleCommand>>()) {
                    await hook.PostHandleAsync(command, cancellationToken);
                }
            }
            catch (Exception exception) {
                foreach (var hook in _scope.Resolve<IEnumerable<IHandleCommandFailure>>()) {
                    await hook.HandleFailureAsync(command, exception, cancellationToken);
                }

                await invoker.HandleFailureAsync(_scope, command, exception, cancellationToken);
                throw;
            }
        }
    }
}
