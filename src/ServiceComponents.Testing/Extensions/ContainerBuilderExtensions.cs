using System;
using Autofac;
using Microsoft.Extensions.Options;
using Serilog;
using ServiceComponents.Application.Senders;
using ServiceComponents.AspNet.Exceptions;
using ServiceComponents.AspNet.Http.Senders;
using HttpRequestOptions = ServiceComponents.AspNet.Http.HttpRequestOptions;

namespace ServiceComponents.Testing.Extensions
{
    public static class ContainerBuilderExtensions
    {
        public static void UseTestPublisher(this ContainerBuilder builder, IPublishEvent publisher)
        {
            builder.RegisterInstance(publisher);
        }

        /// <summary>
        /// Registers HTTP command and query senders posting to <c>/api/generic</c> of a new <typeparamref name="TTestHost"/>.
        /// </summary>
        public static void UseTestSender<TTestHost>(this ContainerBuilder builder, object key = null)
            where TTestHost : ITestHost
        {
            // 0.2 posted to the host root, where no generic controller is routed by convention.
            builder.UseTestSenderAt<TTestHost>(new Uri("http://localhost/api/generic"), key);
        }

        /// <summary>
        /// Registers HTTP command and query senders posting to <paramref name="requestUri"/> of a new <typeparamref name="TTestHost"/>.
        /// Only the path of the URI matters, the requests never leave the process.
        /// </summary>
        public static void UseTestSenderAt<TTestHost>(this ContainerBuilder builder, Uri requestUri, object key = null)
            where TTestHost : ITestHost
        {
            var testHost = Activator.CreateInstance<TTestHost>();

            // The host lives as long as the container that sends to it.
            if (testHost is IAsyncDisposable or IDisposable) {
                builder.RegisterBuildCallback(scope => {
                    if (testHost is IAsyncDisposable asyncDisposable) {
                        scope.Disposer.AddInstanceForAsyncDisposal(asyncDisposable);
                    }
                    else {
                        scope.Disposer.AddInstanceForDisposal((IDisposable)testHost);
                    }
                });
            }

            var commandSenderRegistration = builder.Register(context => new HttpCommandSender(
                context.Resolve<ILogger>(),
                testHost.CreateClient(),
                requestUri,
                context.Resolve<IOptions<HttpRequestOptions>>(), 
                context.Resolve<IExceptionMapperService>()));
            var queryenderRegistration = builder.Register(context => new HttpQuerySender(
                 context.Resolve<ILogger>(),
                 testHost.CreateClient(),
                 requestUri,
                 context.Resolve<IOptions<HttpRequestOptions>>(),
                 context.Resolve<IExceptionMapperService>()));

            if (key == default) {
                commandSenderRegistration.As<ISendHttpCommand>();
                queryenderRegistration.As<ISendHttpQuery>();
            }
            else {
                commandSenderRegistration.Keyed<ISendHttpCommand>(key);
                queryenderRegistration.Keyed<ISendHttpQuery>(key);
            }
        }
    }
}
