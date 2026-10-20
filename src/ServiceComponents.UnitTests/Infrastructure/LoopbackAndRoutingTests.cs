using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Moq;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application;
using ServiceComponents.Application.Mediator;
using ServiceComponents.Application.Senders;
using ServiceComponents.Infrastructure.CorrelationContext;
using ServiceComponents.Infrastructure.Mediator;
using ServiceComponents.Infrastructure.Receivers;
using ServiceComponents.Infrastructure.Receivers.Loopback;
using ServiceComponents.Infrastructure.Senders;
using ServiceComponents.Infrastructure.Senders.Loopback;
using Xunit;

namespace ServiceComponents.UnitTests.Infrastructure
{
    public class LoopbackAndRoutingTests
    {
        public class Captured
        {
            public readonly List<(string message, string correlationId, string causationId, string currentId)> Calls = new();
        }

        public class CapturingHandlers : IHandleCommand<SampleCommand>, IHandleQuery<SampleQuery, string>, IHandleEvent<SampleEvent>
        {
            private readonly Captured _captured;
            private readonly ICorrelation _correlation;

            public CapturingHandlers(Captured captured, ICorrelation correlation)
            {
                _captured = captured;
                _correlation = correlation;
            }

            private void Capture(string message)
            {
                lock (_captured) {
                    _captured.Calls.Add((message, _correlation.CorrelationId, _correlation.CausationId, _correlation.CurrentId));
                }
            }

            public Task HandleAsync(SampleCommand command, CancellationToken cancellationToken = default)
            {
                Capture("command:" + command.Data);
                return Task.CompletedTask;
            }

            public Task<string> HandleAsync(SampleQuery query, CancellationToken cancellationToken = default)
            {
                Capture("query:" + query.Number);
                return Task.FromResult("result:" + query.Number);
            }

            public Task HandleAsync(SampleEvent @event, CancellationToken cancellationToken = default)
            {
                Capture("event:" + @event.Data);
                return Task.CompletedTask;
            }
        }

        private static IContainer BuildLoopback(Captured captured, object key = default)
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance(captured);
            builder.AddMediator();
            builder.RegisterType<CapturingHandlers>().AsImplementedInterfaces();
            builder.AddCorrelationInfo();
            builder.AddReceivers();
            builder.AddLoopbackReceivers();
            builder.AddLoopbackReceiverCorrelationBehavior();
            builder.AddLoopbackCommandSender(key);
            builder.AddLoopbackQuerySender(key);
            builder.AddLoopbackEventPublisher(key);
            return builder.Build();
        }

        [Fact]
        public async Task Loopback_senders_dispatch_in_new_scope_and_derive_correlation_from_parent()
        {
            var captured = new Captured();
            await using var container = BuildLoopback(captured);
            await using var scope = container.BeginLifetimeScope();
            var parent = scope.Resolve<Correlation>();
            parent.CorrelationId = "corr";
            parent.CurrentId = "parent-id";

            await scope.Resolve<ISendCommand>().SendAsync(new SampleCommand("a", "c-1"));
            var result = await scope.Resolve<ISendQuery>().SendAsync(new SampleQuery(2, "q-1"));
            await scope.Resolve<IPublishEvent>().PublishAsync(new SampleEvent("e", "e-1"));
            await scope.Resolve<IPublishEvent>().PublishAsync(new IEvent[] { new SampleEvent("x", "e-2"), new SampleEvent("y", "e-3") });

            Assert.Equal("result:2", result);
            Assert.Equal(new[] {
                ("command:a", "corr", "parent-id", "c-1"),
                ("query:2", "corr", "parent-id", "q-1"),
                // Fixed in 0.3 (known issue #1): event handlers inherit the correlation of the dispatching scope.
                ("event:e", "corr", "parent-id", "e-1"),
                ("event:x", "corr", "parent-id", "e-2"),
                ("event:y", "corr", "parent-id", "e-3")
            }, captured.Calls);
            // Parent correlation is not modified by the child scope.
            Assert.Equal("parent-id", parent.CurrentId);
        }

        [Fact]
        public async Task Loopback_generates_correlation_id_when_parent_has_none()
        {
            var captured = new Captured();
            await using var container = BuildLoopback(captured);
            await using var scope = container.BeginLifetimeScope();

            await scope.Resolve<ISendCommand>().SendAsync(new SampleCommand("a", "c-1"));

            var call = Assert.Single(captured.Calls);
            Assert.True(Guid.TryParse(call.correlationId, out _));
            Assert.Null(call.causationId);
        }

        [Fact]
        public async Task Loopback_with_key_registers_keyed_senders()
        {
            var captured = new Captured();
            await using var container = BuildLoopback(captured, "loopback");

            Assert.False(container.IsRegistered<ISendCommand>());
            await container.ResolveKeyed<ISendCommand>("loopback").SendAsync(new SampleCommand("k"));
            Assert.Equal("result:1", await container.ResolveKeyed<ISendQuery>("loopback").SendAsync(new SampleQuery(1)));
            await container.ResolveKeyed<IPublishEvent>("loopback").PublishAsync(new SampleEvent("k"));

            Assert.Equal(3, captured.Calls.Count);
        }

        [Fact]
        public async Task Loopback_without_correlation_registration_starts_a_new_chain()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterType<Correlation>().AsSelf().InstancePerLifetimeScope();
            builder.AddMediator();
            builder.RegisterInstance(new Captured());
            builder.RegisterType<CapturingHandlers>().AsImplementedInterfaces();
            builder.RegisterType<Correlation>().As<ICorrelation>().IfNotRegistered(typeof(ICorrelation));
            builder.AddReceivers();
            builder.AddLoopbackReceivers();
            builder.AddLoopbackReceiverCorrelationBehavior();
            builder.AddLoopbackCommandSender();
            await using var container = builder.Build();

            // Proxy resolves ICorrelation optionally; passing a correlation-less parent works when ICorrelation exists.
            await container.Resolve<ISendCommand>().SendAsync(new SampleCommand("a"));

            // Fixed in 0.3: without any ICorrelation registration the correlation behavior used to dereference null.
            var bare = new ContainerBuilder();
            bare.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            bare.RegisterType<Correlation>().AsSelf().InstancePerLifetimeScope();
            bare.RegisterInstance(Mock.Of<IReceiveCommand>());
            bare.AddLoopbackReceivers();
            bare.AddLoopbackReceiverCorrelationBehavior();
            bare.AddLoopbackCommandSender();
            await using var bareContainer = bare.Build();
            await bareContainer.Resolve<ISendCommand>().SendAsync(new SampleCommand("a"));
        }

        [Fact]
        public async Task Routers_resolve_keyed_senders()
        {
            var commandSender = new Mock<ISendCommand>();
            var querySender = new Mock<ISendQuery>();
            querySender.Setup(s => s.SendAsync(It.IsAny<SampleQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync("routed");
            var publisher = new RecordingPublisher();

            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance(commandSender.Object).Keyed<ISendCommand>("http");
            builder.RegisterInstance(querySender.Object).Keyed<ISendQuery>("http");
            builder.RegisterInstance(publisher).Keyed<IPublishEvent>("rabbit");
            builder.AddCommandRouter(c => "http");
            builder.AddQueryRouter(q => "http");
            builder.AddEventRouter(e => "rabbit");
            await using var container = builder.Build();

            var command = new SampleCommand("x");
            await container.Resolve<ISendCommand>().SendAsync(command);
            Assert.Equal("routed", await container.Resolve<ISendQuery>().SendAsync(new SampleQuery(1)));
            await container.Resolve<IPublishEvent>().PublishAsync(new SampleEvent("a"));
            await container.Resolve<IPublishEvent>().PublishAsync(new IEvent[] { new SampleEvent("b"), new SampleEvent("c") });

            Assert.IsType<CommandRouter>(container.Resolve<ISendCommand>());
            commandSender.Verify(s => s.SendAsync(command, It.IsAny<CancellationToken>()), Times.Once);
            // The router publishes collections one by one through the single-event overload.
            Assert.Equal(new[] { "SampleEvent:a", "IEvent:b", "IEvent:c" }, publisher.Calls);
        }

        public class RecordingPublisher : IPublishEvent
        {
            public readonly List<string> Calls = new();

            public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
            {
                Calls.Add($"{typeof(TEvent).Name}:{((SampleEvent)(object)@event).Data}");
                return Task.CompletedTask;
            }

            public Task PublishAsync(IEnumerable<IEvent> events, CancellationToken cancellationToken = default)
            {
                Calls.Add("batch");
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task Router_with_unknown_key_throws()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.AddCommandRouter(c => "missing");
            await using var container = builder.Build();

            await Assert.ThrowsAnyAsync<Autofac.Core.DependencyResolutionException>(() => container.Resolve<ISendCommand>().SendAsync(new SampleCommand("x")));
        }

        [Fact]
        public async Task Receivers_forward_to_dispatchers()
        {
            var captured = new Captured();
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance(captured);
            builder.AddMediator();
            builder.AddCorrelationInfo();
            builder.RegisterType<CapturingHandlers>().AsImplementedInterfaces();
            builder.AddReceivers();
            await using var container = builder.Build();

            await container.Resolve<IReceiveCommand>().ReceiveAsync(new SampleCommand("c"));
            Assert.Equal("result:3", await container.Resolve<IReceiveQuery>().ReceiveAsync(new SampleQuery(3)));
            await container.Resolve<IReceiveEvent>().ReceiveAsync(new SampleEvent("e"));

            Assert.Equal(new[] { "command:c", "query:3", "event:e" }, captured.Calls.ConvertAll(c => c.message));
        }
    }
}
