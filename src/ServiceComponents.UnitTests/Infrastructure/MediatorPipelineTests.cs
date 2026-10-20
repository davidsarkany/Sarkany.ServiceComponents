using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Mediator;
using ServiceComponents.Infrastructure.Dispatchers;
using ServiceComponents.Infrastructure.Mediator;
using Xunit;

namespace ServiceComponents.UnitTests.Infrastructure
{
    public class MediatorPipelineTests
    {
        public class Trace : ConcurrentQueue<string>
        {
            public void Add(string entry) => Enqueue(entry);
        }

        public class CommandHandler : IHandleCommand<SampleCommand>
        {
            private readonly Trace _trace;
            public CommandHandler(Trace trace) => _trace = trace;

            public Task HandleAsync(SampleCommand command, CancellationToken cancellationToken = default)
            {
                _trace.Add("handle");
                if (command.Data == "fail") {
                    throw new InvalidOperationException("boom");
                }

                return Task.CompletedTask;
            }
        }

        public class QueryHandler : IHandleQuery<SampleQuery, string>
        {
            private readonly Trace _trace;
            public QueryHandler(Trace trace) => _trace = trace;

            public Task<string> HandleAsync(SampleQuery query, CancellationToken cancellationToken = default)
            {
                _trace.Add("handle");
                if (query.Number < 0) {
                    throw new InvalidOperationException("boom");
                }

                return Task.FromResult($"result-{query.Number}");
            }
        }

        public class EventHandlerA : IHandleEvent<SampleEvent>
        {
            private readonly Trace _trace;
            public EventHandlerA(Trace trace) => _trace = trace;

            public async Task HandleAsync(SampleEvent @event, CancellationToken cancellationToken = default)
            {
                _trace.Add("A-start");
                if (@event.Data == "fail") {
                    throw new InvalidOperationException("boom");
                }

                await Task.Delay(50, cancellationToken);
                _trace.Add("A-end");
            }
        }

        public class EventHandlerB : IHandleEvent<SampleEvent>
        {
            private readonly Trace _trace;
            public EventHandlerB(Trace trace) => _trace = trace;

            public Task HandleAsync(SampleEvent @event, CancellationToken cancellationToken = default)
            {
                _trace.Add("B");
                return Task.CompletedTask;
            }
        }

        public class Hooks :
            IPreHandleCommand, IPreHandleCommand<SampleCommand>, IPostHandleCommand, IPostHandleCommand<SampleCommand>,
            IHandleCommandFailure, IHandleCommandFailure<SampleCommand>,
            IPreHandleQuery, IPreHandleQuery<SampleQuery, string>, IPostHandleQuery, IPostHandleQuery<SampleQuery, string>,
            IHandleQueryFailure, IHandleQueryFailure<SampleQuery, string>,
            IPreHandleEvent, IPreHandleEvent<SampleEvent>, IPostHandleEvent, IPostHandleEvent<SampleEvent>,
            IHandleEventFailure, IHandleEventFailure<SampleEvent>
        {
            private readonly Trace _trace;
            public Hooks(Trace trace) => _trace = trace;

            private Task Add(string entry)
            {
                _trace.Add(entry);
                return Task.CompletedTask;
            }

            public Task PreHandleAsync(ICommand command, CancellationToken cancellationToken = default) => Add("pre");
            public Task PreHandleAsync(SampleCommand command, CancellationToken cancellationToken = default) => Add("pre<T>");
            public Task PostHandleAsync(ICommand command, CancellationToken cancellationToken = default) => Add("post");
            public Task PostHandleAsync(SampleCommand command, CancellationToken cancellationToken = default) => Add("post<T>");
            public Task HandleFailureAsync(ICommand command, Exception exception, CancellationToken cancellationToken = default) => Add($"fail:{exception.Message}");
            public Task HandleFailureAsync(SampleCommand command, Exception exception, CancellationToken cancellationToken = default) => Add($"fail<T>:{exception.Message}");

            public Task PreHandleAsync(IQuery query, CancellationToken cancellationToken = default) => Add("pre");
            public Task PreHandleAsync(SampleQuery query, CancellationToken cancellationToken = default) => Add("pre<T>");
            public Task PostHandleAsync(IQuery query, object result, CancellationToken cancellationToken = default) => Add($"post:{result}");

            public Task<string> PostHandleAsync(SampleQuery query, string result, CancellationToken cancellationToken = default)
            {
                _trace.Add($"post<T>:{result}");
                return Task.FromResult("replaced");
            }

            public Task HandleFailureAsync(IQuery query, Exception exception, CancellationToken cancellationToken = default) => Add($"fail:{exception.Message}");
            public Task HandleFailureAsync(SampleQuery query, Exception exception, CancellationToken cancellationToken = default) => Add($"fail<T>:{exception.Message}");

            public Task PreHandleAsync(IEvent @event, CancellationToken cancellationToken = default) => Add("pre");
            public Task PreHandleAsync(SampleEvent @event, CancellationToken cancellationToken = default) => Add("pre<T>");
            public Task PostHandleAsync(IEvent @event, CancellationToken cancellationToken = default) => Add("post");
            public Task PostHandleAsync(SampleEvent @event, CancellationToken cancellationToken = default) => Add("post<T>");
            public Task HandleFailureAsync(IEvent @event, Exception exception, CancellationToken cancellationToken = default) => Add($"fail:{exception.Message}");
            public Task HandleFailureAsync(SampleEvent @event, Exception exception, CancellationToken cancellationToken = default) => Add($"fail<T>:{exception.Message}");
        }

        private static IContainer Build(Trace trace, bool behaviors = true, bool handlers = true)
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance(trace);

            builder.AddMediator();
            if (handlers) {
                builder.RegisterType<CommandHandler>().As<IHandleCommand<SampleCommand>>();
                builder.RegisterType<QueryHandler>().As<IHandleQuery<SampleQuery, string>>();
                builder.RegisterType<EventHandlerA>().As<IHandleEvent<SampleEvent>>();
                builder.RegisterType<EventHandlerB>().As<IHandleEvent<SampleEvent>>();
            }

            if (behaviors) {
                builder.AddMediatorBehavior();
                builder.RegisterType<Hooks>().AsImplementedInterfaces();
            }

            return builder.Build();
        }

        [Fact]
        public async Task Command_hook_order()
        {
            var trace = new Trace();
            await using var container = Build(trace);

            await container.Resolve<IDispatchCommand>().DispatchAsync(new SampleCommand("ok"));

            Assert.Equal(new[] { "pre", "pre<T>", "handle", "post<T>", "post" }, trace.ToArray());
        }

        [Fact]
        public async Task Command_failure_hooks_run_and_exception_is_rethrown()
        {
            var trace = new Trace();
            await using var container = Build(trace);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                container.Resolve<IDispatchCommand>().DispatchAsync(new SampleCommand("fail")));

            Assert.Equal("boom", error.Message);
            Assert.Equal(new[] { "pre", "pre<T>", "handle", "fail:boom", "fail<T>:boom" }, trace.ToArray());
        }

        [Fact]
        public async Task Query_hook_order_and_post_hook_cannot_replace_result()
        {
            var trace = new Trace();
            await using var container = Build(trace);

            var result = await container.Resolve<IDispatchQuery>().DispatchAsync(new SampleQuery(3));

            Assert.Equal("result-3", result);
            Assert.Equal(new[] { "pre", "pre<T>", "handle", "post:result-3", "post<T>:result-3" }, trace.ToArray());
        }

        [Fact]
        public async Task Query_failure_hooks_run()
        {
            var trace = new Trace();
            await using var container = Build(trace);

            await Assert.ThrowsAsync<InvalidOperationException>(() => container.Resolve<IDispatchQuery>().DispatchAsync(new SampleQuery(-1)));

            Assert.Equal(new[] { "pre", "pre<T>", "handle", "fail:boom", "fail<T>:boom" }, trace.ToArray());
        }

        [Fact]
        public async Task Event_hook_order_and_handlers_run_concurrently()
        {
            var trace = new Trace();
            await using var container = Build(trace);

            await container.Resolve<IDispatchEvent>().DispatchAsync(new SampleEvent("ok"));

            var entries = trace.ToArray();
            Assert.Equal(new[] { "pre", "pre<T>" }, entries.Take(2));
            Assert.Equal(new[] { "post", "post<T>" }, entries.Skip(entries.Length - 2));
            // Handler B completes while A is still awaiting: handlers are started together (Task.WhenAll).
            var handlerEntries = entries.Skip(2).Take(3).ToArray();
            Assert.Equal(new[] { "A-start", "B", "A-end" }, handlerEntries);
        }

        [Fact]
        public async Task Event_failure_hooks_run()
        {
            var trace = new Trace();
            await using var container = Build(trace);

            await Assert.ThrowsAsync<InvalidOperationException>(() => container.Resolve<IDispatchEvent>().DispatchAsync(new SampleEvent("fail")));

            Assert.Contains("fail:boom", trace);
            Assert.Contains("fail<T>:boom", trace);
            Assert.DoesNotContain("post", trace);
        }

        [Fact]
        public async Task Missing_command_handler_throws()
        {
            await using var container = Build(new Trace(), behaviors: false, handlers: false);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => container.Resolve<IDispatchCommand>().DispatchAsync(new SampleCommand("x")));

            Assert.Equal("No handler registered for SampleCommand", error.Message);
        }

        [Fact]
        public async Task Missing_query_handler_throws()
        {
            await using var container = Build(new Trace(), behaviors: false, handlers: false);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => container.Resolve<IDispatchQuery>().DispatchAsync(new SampleQuery(1)));

            Assert.Equal("No handler found for SampleQuery", error.Message);
        }

        [Fact]
        public async Task Missing_event_handlers_is_a_noop()
        {
            var trace = new Trace();
            await using var container = Build(trace, behaviors: false, handlers: false);

            await container.Resolve<IDispatchEvent>().DispatchAsync(new SampleEvent("x"));

            Assert.Empty(trace);
        }

        [Fact]
        public async Task Dispatch_uses_runtime_type_of_message()
        {
            var trace = new Trace();
            await using var container = Build(trace, behaviors: false);

            ICommand command = new SampleCommand("ok");
            await container.Resolve<IDispatchCommand>().DispatchAsync(command);

            Assert.Equal(new[] { "handle" }, trace.ToArray());
        }

        [Fact]
        public async Task Event_handlers_resolve_in_child_scope()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.AddMediator();
            builder.RegisterType<ScopeCapturingHandler>().As<IHandleEvent<SampleEvent>>();
            builder.RegisterType<ScopedDependency>().InstancePerLifetimeScope();
            await using var container = builder.Build();
            await using var scope = container.BeginLifetimeScope();
            var outer = scope.Resolve<ScopedDependency>();

            await scope.Resolve<IDispatchEvent>().DispatchAsync(new SampleEvent("x"));

            Assert.NotNull(ScopeCapturingHandler.Last);
            Assert.NotSame(outer, ScopeCapturingHandler.Last);
        }

        [Fact]
        public void AddMediator_scans_assemblies_for_handlers_and_hooks()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.AddMediator(typeof(ReferenceApplication.Application.TestCommandHandler).Assembly);
            builder.AddMediatorBehavior(typeof(ReferenceApplication.Application.TestCommandHandler).Assembly);
            using var container = builder.Build();

            Assert.True(container.IsRegistered<IHandleCommand<ReferenceApplication.Api.TestCommand>>());
            Assert.IsType<CommandDispatchBehavior>(container.Resolve<IDispatchCommand>());
            Assert.IsType<QueryDispatchBehavior>(container.Resolve<IDispatchQuery>());
            Assert.IsType<EventDispatchBehavior>(container.Resolve<IDispatchEvent>());
        }

        [Fact]
        public async Task Non_public_handler_is_dispatched()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.AddMediator();
            builder.RegisterType<PrivateHandler>().As<IHandleCommand<SampleCommand>>();
            await using var container = builder.Build();

            // Fixed in 0.3 (known issue #21): dispatch went through 'dynamic', which could not call non-public handlers.
            await container.Resolve<IDispatchCommand>().DispatchAsync(new SampleCommand("x"));
        }

        public class MultiHandler : IHandleCommand<OtherCommand>, IHandleEvent<SampleEvent>
        {
            public Task HandleAsync(OtherCommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task HandleAsync(SampleEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        [Fact]
        public void Class_with_several_handler_interfaces_is_registered_once()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.AddMediator(typeof(MediatorPipelineTests).Assembly);
            using var container = builder.Build();

            // Fixed in 0.3 (known issue #20): the class used to be registered once per handler interface kind.
            var registrations = container.ComponentRegistry.RegistrationsFor(new Autofac.Core.TypedService(typeof(IHandleEvent<SampleEvent>)));
            Assert.Single(registrations, registration => registration.Activator.LimitType == typeof(MultiHandler));
        }

        private class PrivateHandler : IHandleCommand<SampleCommand>
        {
            public Task HandleAsync(SampleCommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        public class ScopedDependency { }

        public class ScopeCapturingHandler : IHandleEvent<SampleEvent>
        {
            public static ScopedDependency Last;
            public ScopeCapturingHandler(ScopedDependency dependency) => Last = dependency;
            public Task HandleAsync(SampleEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }
    }
}
