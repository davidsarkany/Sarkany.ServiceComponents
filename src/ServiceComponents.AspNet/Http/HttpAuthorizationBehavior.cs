using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace ServiceComponents.AspNet.Http
{
    public abstract class HttpAuthorizationBehavior
    {
        protected readonly ILifetimeScope Scope;
        private readonly IAuthorizationPolicyProvider _policyProvider;
        private readonly IAuthorizationService _authorizationService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        protected HttpAuthorizationBehavior(ILogger log, ILifetimeScope scope, IAuthorizationPolicyProvider policyProvider, IAuthorizationService authorizationService, IHttpContextAccessor httpContextAccessor)
        {
            Scope = scope;
            _policyProvider = policyProvider;
            _authorizationService = authorizationService;
            _httpContextAccessor = httpContextAccessor;
        }

        protected async Task AuthorizeRequest(object handler)
        {
            await AuthorizeHandler(handler.GetType());
        }

        // The handler class whose [Authorize] attributes apply. Taken from the registration when possible, so the handler
        // is not instantiated just to read its attributes; resolved otherwise (e.g. lambda registrations). Null if missing.
        internal Type HandlerType(Type serviceType)
        {
            if (Scope.ComponentRegistry.TryGetRegistration(new Autofac.Core.TypedService(serviceType), out var registration)
                && registration.Activator.LimitType is { IsInterface: false, IsAbstract: false } limitType) {
                return limitType;
            }

            return Scope.ResolveOptional(serviceType)?.GetType();
        }

        internal async Task AuthorizeHandler(Type handlerType)
        {
            var authorizeData = (IAuthorizeData[])Attribute.GetCustomAttributes(handlerType, typeof(AuthorizeAttribute));
            var policy = await AuthorizationPolicy.CombineAsync(_policyProvider, authorizeData);

            if (policy != null) {
                var authorizationResult = await _authorizationService.AuthorizeAsync(_httpContextAccessor?.HttpContext?.User!, null, policy);
                if (!authorizationResult.Succeeded) {
                    throw new UnauthorizedAccessException(string.Join("\r\n", authorizationResult.Failure.FailedRequirements.Select(requirement => requirement.ToString())));
                }
            }
        }
    }
}