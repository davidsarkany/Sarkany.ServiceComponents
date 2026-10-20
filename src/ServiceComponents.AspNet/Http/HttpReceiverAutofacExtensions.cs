using Autofac;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace ServiceComponents.AspNet.Http
{
    public static class HttpReceiverAutofacExtensions
    {
        public static ContainerBuilder AddHttpReceiverCorrelationBehavior(this ContainerBuilder builder)
        {
            builder.RegisterDecorator<HttpCommandReceiverCorrelationBehavior, IReceiveHttpCommand>();
            builder.RegisterDecorator<HttpQueryReceiverCorrelationBehavior, IReceiveHttpQuery>();
            builder.RegisterDecorator<HttpEventReceiverCorrelationBehavior, IReceiveHttpEvent>();

            return builder;
        }

        public static ContainerBuilder AddHttpReceiverAuthorizationBehavior(this ContainerBuilder builder)
        {
            // Decorates the HTTP receiver chain itself, so the correlation behavior and HTTP receiver keep running.
            builder.RegisterDecorator<IReceiveHttpCommand>((context, _, inner) => new HttpCommandAuthorizationBehavior(
                context.Resolve<ILogger>(), context.Resolve<ILifetimeScope>(), context.Resolve<IAuthorizationPolicyProvider>(),
                context.Resolve<IAuthorizationService>(), context.Resolve<IHttpContextAccessor>(), inner));
            builder.RegisterDecorator<IReceiveHttpQuery>((context, _, inner) => new HttpQueryAuthorizationBehavior(
                context.Resolve<ILogger>(), context.Resolve<ILifetimeScope>(), context.Resolve<IAuthorizationPolicyProvider>(),
                context.Resolve<IAuthorizationService>(), context.Resolve<IHttpContextAccessor>(), inner));

            return builder;
        }
    }
}