using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Serilog;
using ServiceComponents.Application.Mediator;
using ServiceComponents.AspNet.Http;
using ServiceComponents.Infrastructure.CorrelationContext;
using ServiceComponents.Infrastructure.Receivers;
using ServiceComponents.UnitTests.Fixtures;
using Xunit;

namespace ServiceComponents.UnitTests.AspNet
{
    /// <summary>
    /// The public constructors (forwarding to IReceiveCommand/IReceiveQuery) are kept for code that registers the behaviors by hand.
    /// </summary>
    public class AuthorizationBehaviorTests
    {
        private static (IContainer container, ServiceProvider services, HttpContextAccessor accessor) Build(string role, Action<ContainerBuilder> register)
        {
            var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
            var accessor = new HttpContextAccessor {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test")) }
            };
            var builder = new ContainerBuilder();
            register(builder);
            return (builder.Build(), services, accessor);
        }

        private static HttpCommandAuthorizationBehavior CommandBehavior((IContainer container, ServiceProvider services, HttpContextAccessor accessor) host, IReceiveCommand next) =>
            new(Mock.Of<ILogger>(), host.container, host.services.GetRequiredService<IAuthorizationPolicyProvider>(),
                host.services.GetRequiredService<IAuthorizationService>(), host.accessor, next);

        private static HttpQueryAuthorizationBehavior QueryBehavior((IContainer container, ServiceProvider services, HttpContextAccessor accessor) host, IReceiveQuery next) =>
            new(Mock.Of<ILogger>(), host.container, host.services.GetRequiredService<IAuthorizationPolicyProvider>(),
                host.services.GetRequiredService<IAuthorizationService>(), host.accessor, next);

        // Registered against the interface only, so the handler class is found by resolving it.
        private static void LambdaHandlers(ContainerBuilder builder)
        {
            builder.Register<IHandleCommand<SecuredCommand>>(_ => new SecuredCommandHandler(new ReceivedRequests(), new Correlation()));
            builder.Register<IHandleQuery<SecuredQuery, string>>(_ => new SecuredQueryHandler());
        }

        [Fact]
        public async Task Command_behavior_authorizes_lambda_registered_handlers()
        {
            var next = new Mock<IReceiveCommand>();
            var command = new SecuredCommand();

            await CommandBehavior(Build("admin", LambdaHandlers), next.Object).ReceiveAsync(command);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CommandBehavior(Build("user", LambdaHandlers), next.Object).ReceiveAsync(command));

            next.Verify(receiver => receiver.ReceiveAsync(command, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Query_behavior_authorizes_lambda_registered_handlers()
        {
            var next = new Mock<IReceiveQuery>();
            next.Setup(receiver => receiver.ReceiveAsync(It.IsAny<SecuredQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync("secret");

            Assert.Equal("secret", await QueryBehavior(Build("admin", LambdaHandlers), next.Object).ReceiveAsync(new SecuredQuery()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => QueryBehavior(Build("user", LambdaHandlers), next.Object).ReceiveAsync(new SecuredQuery()));
        }

        [Fact]
        public async Task Missing_handlers_are_reported()
        {
            var host = Build("admin", _ => { });

            Assert.Contains("No handler registered for", (await Assert.ThrowsAsync<InvalidOperationException>(() => CommandBehavior(host, Mock.Of<IReceiveCommand>()).ReceiveAsync(new SecuredCommand()))).Message);
            Assert.Contains("No handler found for", (await Assert.ThrowsAsync<InvalidOperationException>(() => QueryBehavior(host, Mock.Of<IReceiveQuery>()).ReceiveAsync(new SecuredQuery()))).Message);
        }
    }
}
