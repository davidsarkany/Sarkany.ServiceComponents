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
    /// Checks the <c>[Authorize]</c> attributes of the query's handler class against the current user before receiving it.
    /// </summary>
    public class HttpQueryAuthorizationBehavior : HttpAuthorizationBehavior, IReceiveHttpQuery
    {
        private readonly IReceiveQuery _next;
        private readonly IReceiveHttpQuery _nextHttp;

        public HttpQueryAuthorizationBehavior(ILogger log, ILifetimeScope scope, IAuthorizationPolicyProvider policyProvider, IAuthorizationService authorizationService, IHttpContextAccessor httpContextAccessor, IReceiveQuery next)
            : base(log, scope, policyProvider, authorizationService, httpContextAccessor)
        {
            _next = next;
        }

        // Used by AddHttpReceiverAuthorizationBehavior, see HttpCommandAuthorizationBehavior.
        internal HttpQueryAuthorizationBehavior(ILogger log, ILifetimeScope scope, IAuthorizationPolicyProvider policyProvider, IAuthorizationService authorizationService, IHttpContextAccessor httpContextAccessor, IReceiveHttpQuery next)
            : base(log, scope, policyProvider, authorizationService, httpContextAccessor)
        {
            _nextHttp = next;
        }

        public async Task<TResult> ReceiveAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            var handlerType = HandlerType(typeof(IHandleQuery<,>).MakeGenericType(query.GetType(), typeof(TResult))) ?? throw new InvalidOperationException($"No handler found for {query.DisplayName()}");
            await AuthorizeHandler(handlerType);
            return _nextHttp != null
                ? await _nextHttp.ReceiveAsync(query, cancellationToken)
                : await _next.ReceiveAsync(query, cancellationToken);
        }
    }
}
