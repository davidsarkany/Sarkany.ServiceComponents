using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Mediator;
using ServiceComponents.Core.Extensions;
using ServiceComponents.Infrastructure.Receivers;

namespace ServiceComponents.AspNet.Http
{
    /// <summary>
    /// Checks the <c>[Authorize]</c> attributes of the command's handler class against the current user before receiving it.
    /// </summary>
    public class HttpCommandAuthorizationBehavior : HttpAuthorizationBehavior, IReceiveHttpCommand
    {
        private readonly IReceiveCommand _next;
        private readonly IReceiveHttpCommand _nextHttp;

        public HttpCommandAuthorizationBehavior(ILogger log, ILifetimeScope scope, IAuthorizationPolicyProvider policyProvider, IAuthorizationService authorizationService, IHttpContextAccessor httpContextAccessor, IReceiveCommand next)
            : base(log, scope, policyProvider, authorizationService, httpContextAccessor)
        {
            _next = next;
        }

        // Used by AddHttpReceiverAuthorizationBehavior: decorates the HTTP receiver chain, so the HTTP correlation behavior
        // and the HTTP receiver still run (the public constructor skipped them by forwarding to IReceiveCommand).
        internal HttpCommandAuthorizationBehavior(ILogger log, ILifetimeScope scope, IAuthorizationPolicyProvider policyProvider, IAuthorizationService authorizationService, IHttpContextAccessor httpContextAccessor, IReceiveHttpCommand next)
            : base(log, scope, policyProvider, authorizationService, httpContextAccessor)
        {
            _nextHttp = next;
        }

        public async Task ReceiveAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand
        {
            var handlerType = HandlerType(typeof(IHandleCommand<>).MakeGenericType(command.GetType())) ?? throw new InvalidOperationException($"No handler registered for {command.DisplayName()}");
            await AuthorizeHandler(handlerType);
            if (_nextHttp != null) {
                await _nextHttp.ReceiveAsync(command, cancellationToken);
            }
            else {
                await _next.ReceiveAsync(command, cancellationToken);
            }
        }
    }
}
