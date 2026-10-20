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
    /// Dispatches a command to the single <c>IHandleCommand&lt;T&gt;</c> registered for its runtime type.
    /// </summary>
    public class CommandDispatcher : IDispatchCommand
    {
        private readonly ILogger _log;
        private readonly ILifetimeScope _scope;

        public CommandDispatcher(ILogger log, ILifetimeScope scope)
        {
            _log = log;
            _scope = scope;
        }

        // Async so that a missing handler or a synchronously throwing handler faults the returned task, as before.
        public async Task DispatchAsync<T>(T command, CancellationToken cancellationToken = default) where T : ICommand
        {
            var invoker = CommandInvoker.For(command);
            var handler = invoker.ResolveHandler(_scope) ?? throw new InvalidOperationException($"No handler registered for {command.DisplayName()}");
            _log.Verbose("Dispatching {commandType} to {handlerType}", command.DisplayName(), handler.DisplayName());
            await invoker.HandleAsync(handler, command, cancellationToken);
        }
    }
}
