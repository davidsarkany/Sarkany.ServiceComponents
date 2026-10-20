using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application;
using ServiceComponents.Application.Mediator;
using ServiceComponents.Infrastructure.Behaviors.CommandConstraints;
using ServiceComponents.Infrastructure.Behaviors.Logging;
using ServiceComponents.Infrastructure.Behaviors.Stopwatch;
using ServiceComponents.Infrastructure.CorrelationContext;
using ServiceComponents.Infrastructure.Dispatchers;
using ServiceComponents.Infrastructure.Mediator;
using ServiceComponents.Infrastructure.Receivers;
using ServiceComponents.Infrastructure.Validation;
using ServiceComponents.UnitTests.Fixtures;
using Xunit;

namespace ServiceComponents.UnitTests.Infrastructure
{
    public class BehaviorTests
    {
        public class Handlers : IHandleCommand<SampleCommand>, IHandleQuery<SampleQuery, string>, IHandleEvent<SampleEvent>
        {
            private readonly ILogger _log;
            public Handlers(ILogger log) => _log = log;

            public Task HandleAsync(SampleCommand command, CancellationToken cancellationToken = default)
            {
                _log.Information("in handler");
                return command.Data == "fail" ? throw new InvalidOperationException("boom") : Task.CompletedTask;
            }

            public Task<string> HandleAsync(SampleQuery query, CancellationToken cancellationToken = default) =>
                query.Number < 0 ? throw new InvalidOperationException("boom") : Task.FromResult("r" + query.Number);

            public Task HandleAsync(SampleEvent @event, CancellationToken cancellationToken = default) =>
                @event.Data == "fail" ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        }

        public class SampleCommandValidator : AbstractValidator<SampleCommand>
        {
            public SampleCommandValidator() => RuleFor(c => c.Data).NotEmpty().WithMessage("Data required");
        }

        public class SampleQueryValidator : AbstractValidator<SampleQuery>
        {
            public SampleQueryValidator() => RuleFor(q => q.Number).LessThan(100);
        }

        private static IContainer Build(TestLog log, Action<ContainerBuilder> configure)
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(log.Logger).As<ILogger>();
            builder.AddMediator();
            builder.RegisterType<Handlers>().AsImplementedInterfaces();
            configure(builder);
            return builder.Build();
        }

        [Fact]
        public async Task Log_behavior_logs_success_and_failure()
        {
            var log = new TestLog();
            await using var container = Build(log, b => {
                b.AddMediatorBehavior();
                b.AddLogBehavior();
            });
            var commands = container.Resolve<IDispatchCommand>();
            var queries = container.Resolve<IDispatchQuery>();
            var events = container.Resolve<IDispatchEvent>();

            await commands.DispatchAsync(new SampleCommand("ok"));
            await queries.DispatchAsync(new SampleQuery(1));
            await events.DispatchAsync(new SampleEvent("ok"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => commands.DispatchAsync(new SampleCommand("fail")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => queries.DispatchAsync(new SampleQuery(-1)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => events.DispatchAsync(new SampleEvent("fail")));

            Assert.Equal(LogEventLevel.Verbose, log.Events.First(e => e.MessageTemplate.Text == "Handling {commandType}").Level);
            Assert.True(log.Events.First(e => e.MessageTemplate.Text == "Handling {commandType}").Properties.ContainsKey("commandData"));
            Assert.Contains("Handling {commandType} succeeded", log.Templates);
            Assert.Equal(LogEventLevel.Error, log.Single("Handling {commandType} failed").Level);

            Assert.True(log.Events.First(e => e.MessageTemplate.Text == "Handling {queryType}").Properties.ContainsKey("queryData"));
            Assert.True(log.Single("Handling {queryType} succeeded").Properties.ContainsKey("queryResult"));
            Assert.Equal(LogEventLevel.Error, log.Single("Handling {queryType} failed").Level);

            var eventLog = log.Events.First(e => e.MessageTemplate.Text == "Handling {eventType}");
            Assert.Equal(LogEventLevel.Debug, eventLog.Level);
            Assert.True(eventLog.Properties.ContainsKey("eventData"));
            Assert.Contains("Handling {eventType} succeeded", log.Templates);
            Assert.Equal(LogEventLevel.Error, log.Single("Handling {eventType} failed").Level);
        }

        [Fact]
        public async Task Stopwatch_behavior_logs_duration()
        {
            var log = new TestLog();
            await using var container = Build(log, b => b.AddStopwatchBehavior());

            await container.Resolve<IDispatchCommand>().DispatchAsync(new SampleCommand("ok"));
            await container.Resolve<IDispatchQuery>().DispatchAsync(new SampleQuery(1));
            await container.Resolve<IDispatchEvent>().DispatchAsync(new SampleEvent("ok"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => container.Resolve<IDispatchCommand>().DispatchAsync(new SampleCommand("fail")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => container.Resolve<IDispatchQuery>().DispatchAsync(new SampleQuery(-1)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => container.Resolve<IDispatchEvent>().DispatchAsync(new SampleEvent("fail")));

            foreach (var template in new[] {
                "{command} processed in {duration} ms", "{query} processed in {duration} ms", "{event} processed in {duration} ms",
                "{command} failed in {duration} ms", "{query} failed in {duration} ms", "{event} failed in {duration} ms"
            }) {
                var entry = log.Single(template);
                Assert.Equal(LogEventLevel.Verbose, entry.Level);
                Assert.True(entry.Properties.ContainsKey("duration"));
            }

            Assert.Equal("\"SampleCommand\"", log.Single("{command} processed in {duration} ms").Properties["command"].ToString());
        }

        [Fact]
        public async Task Validation_behavior_throws_validation_exception()
        {
            await using var container = Build(new TestLog(), b => {
                b.AddMediatorBehavior();
                b.AddValidationBehavior(typeof(BehaviorTests).Assembly);
            });

            var error = await Assert.ThrowsAsync<ValidationException>(() => container.Resolve<IDispatchCommand>().DispatchAsync(new SampleCommand("")));
            Assert.Equal("Data required", error.Errors.Single().ErrorMessage);
            await Assert.ThrowsAsync<ValidationException>(() => container.Resolve<IDispatchQuery>().DispatchAsync(new SampleQuery(500)));

            await container.Resolve<IDispatchCommand>().DispatchAsync(new SampleCommand("ok"));
            Assert.Equal("r1", await container.Resolve<IDispatchQuery>().DispatchAsync(new SampleQuery(1)));
        }

        [Fact]
        public async Task Validation_requires_mediator_behavior_to_run()
        {
            await using var container = Build(new TestLog(), b => b.AddValidationBehavior(typeof(BehaviorTests).Assembly));

            // Without AddMediatorBehavior the generic pre-handlers are never invoked.
            await container.Resolve<IDispatchCommand>().DispatchAsync(new SampleCommand(""));
        }

        [Fact]
        public async Task Correlation_enricher_pushes_log_context_properties()
        {
            var log = new TestLog();
            await using var container = Build(log, b => {
                b.AddCorrelationInfo();
                b.AddReceivers();
                b.AddReceiverCorrelationLogEnricherBehavior();
                b.RegisterInstance(Microsoft.Extensions.Options.Options.Create(new CorrelationLogOptions()));
            });
            await using var scope = container.BeginLifetimeScope();
            var correlation = scope.Resolve<Correlation>();
            correlation.CorrelationId = "corr";
            correlation.CausationId = "cause";

            await scope.Resolve<IReceiveCommand>().ReceiveAsync(new SampleCommand("ok", "cmd-1"));

            var entry = log.Single("in handler");
            Assert.Equal("\"corr\"", entry.Properties["correlation-id"].ToString());
            Assert.Equal("\"cause\"", entry.Properties["causation-id"].ToString());
            Assert.Equal("\"cmd-1\"", entry.Properties["command-id"].ToString());
        }

        [Fact]
        public async Task Correlation_enricher_skips_empty_values_and_uses_custom_names()
        {
            var log = new TestLog();
            await using var container = Build(log, b => {
                b.AddCorrelationInfo();
                b.AddReceivers();
                b.AddReceiverCorrelationLogEnricherBehavior();
                b.RegisterInstance(Microsoft.Extensions.Options.Options.Create(new CorrelationLogOptions {
                    QueryIdPropertyName = "qid", EventIdPropertyName = "eid"
                }));
            });

            await using var scope = container.BeginLifetimeScope();
            Assert.Equal("r1", await scope.Resolve<IReceiveQuery>().ReceiveAsync(new SampleQuery(1, "q-1")));
            await scope.Resolve<IReceiveEvent>().ReceiveAsync(new SampleEvent("ok", "e-1"));
            await scope.Resolve<IReceiveCommand>().ReceiveAsync(new SampleCommand("ok", "c-1"));

            var entry = log.Single("in handler");
            Assert.False(entry.Properties.ContainsKey("correlation-id"));
            Assert.False(entry.Properties.ContainsKey("causation-id"));
            // Pushed properties are not disposed, but AsyncLocal changes inside an async method do not flow back to the caller,
            // so they do not leak into later receives.
            Assert.False(entry.Properties.ContainsKey("qid"));
            Assert.False(entry.Properties.ContainsKey("eid"));
            Assert.Equal("\"c-1\"", entry.Properties["command-id"].ToString());
        }

        [Fact]
        public void Correlation_options_defaults()
        {
            var options = new CorrelationLogOptions();

            Assert.Equal(("correlation-id", "causation-id", "command-id", "query-id", "event-id"),
                (options.CorrelationIdPropertyName, options.CausationIdPropertyName, options.CommandIdPropertyName, options.QueryIdPropertyName, options.EventIdPropertyName));
        }

        [Fact]
        public void Correlation_is_scoped_and_shared_between_interface_and_class()
        {
            var builder = new ContainerBuilder();
            builder.AddCorrelationInfo();
            using var container = builder.Build();
            using var scope1 = container.BeginLifetimeScope();
            using var scope2 = container.BeginLifetimeScope();

            Assert.Same(scope1.Resolve<Correlation>(), scope1.Resolve<ICorrelation>());
            Assert.NotSame(scope1.Resolve<Correlation>(), scope2.Resolve<Correlation>());
        }

        private static (RequestExecutionConstraintBehavior behavior, IDistributedCache cache) Constraint(int max)
        {
            var cache = new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));
            var behavior = new RequestExecutionConstraintBehavior(cache,
                request => request is SampleCommand c ? new[] { "key:" + c.Data } : request is SampleQuery ? new[] { "query" } : Array.Empty<string>(),
                (key, value) => value < max,
                key => TimeSpan.FromMinutes(1));
            return (behavior, cache);
        }

        private static int? Counter(IDistributedCache cache, string key)
        {
            var bytes = cache.Get(key);
            return bytes == null ? null : BitConverter.ToInt32(bytes, 0);
        }

        [Fact]
        public async Task Constraint_increments_and_rejects_over_limit()
        {
            var (behavior, cache) = Constraint(2);

            await behavior.PreHandleAsync((ICommand)new SampleCommand("a"));
            await behavior.PreHandleAsync((ICommand)new SampleCommand("a"));
            Assert.Equal(2, Counter(cache, "key:a"));

            var error = await Assert.ThrowsAsync<CommandConstraintException>(() => behavior.PreHandleAsync((ICommand)new SampleCommand("a")));
            Assert.Equal("Cannot execute request because of constraints", error.Message);
        }

        [Fact]
        public async Task Constraint_completion_decrements_and_removes_the_key_at_zero()
        {
            var (behavior, cache) = Constraint(5);
            await behavior.PreHandleAsync((ICommand)new SampleCommand("a"));
            await behavior.PreHandleAsync((ICommand)new SampleCommand("a"));

            // Fixed (known issue #5): the decrement used to be inverted and a second completion threw.
            await behavior.PostHandleAsync((ICommand)new SampleCommand("a"));
            Assert.Equal(1, Counter(cache, "key:a"));
            await behavior.PostHandleAsync((ICommand)new SampleCommand("a"));
            Assert.Null(Counter(cache, "key:a"));
            await behavior.PostHandleAsync((ICommand)new SampleCommand("a"));
            Assert.Null(Counter(cache, "key:a"));
        }

        [Fact]
        public async Task Constraint_rejection_rolls_back_earlier_keys()
        {
            var cache = new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));
            var behavior = new RequestExecutionConstraintBehavior(cache, _ => new[] { "free", "busy" }, (key, _) => key != "busy", _ => null);

            await Assert.ThrowsAsync<CommandConstraintException>(() => behavior.PreHandleAsync((ICommand)new SampleCommand("a")));

            // Fixed (known issue #5): "free" used to stay incremented.
            Assert.Null(Counter(cache, "free"));
        }

        [Fact]
        public async Task Constraint_failure_handling()
        {
            var (behavior, cache) = Constraint(1);
            var first = new SampleCommand("a");
            var rejected = new SampleCommand("a");
            await behavior.PreHandleAsync((ICommand)first);

            // The rejected request did not take a slot, so its failure does not give one back.
            var rejection = await Assert.ThrowsAsync<CommandConstraintException>(() => behavior.PreHandleAsync((ICommand)rejected));
            await behavior.HandleFailureAsync((ICommand)rejected, rejection);
            Assert.Equal(1, Counter(cache, "key:a"));

            // A constraint rejection of a nested request still releases the outer request (fixed).
            await behavior.HandleFailureAsync((ICommand)first, rejection, new CancellationToken(true));
            Assert.Null(Counter(cache, "key:a"));
        }

        [Fact]
        public async Task Constraint_query_paths_and_requests_without_keys()
        {
            var (behavior, cache) = Constraint(1);

            await behavior.PreHandleAsync((IQuery)new SampleQuery(1));
            await Assert.ThrowsAsync<CommandConstraintException>(() => behavior.PreHandleAsync((IQuery)new SampleQuery(1)));
            await behavior.PostHandleAsync(new SampleQuery(1), "r");
            Assert.Null(Counter(cache, "query"));
            await behavior.PreHandleAsync((IQuery)new SampleQuery(1));
            await behavior.HandleFailureAsync(new SampleQuery(1), new Exception());
            Assert.Null(Counter(cache, "query"));

            var keyless = new RequestExecutionConstraintBehavior(cache, _ => null, (_, _) => false, _ => null);
            await keyless.PreHandleAsync((ICommand)new SampleCommand("x"));
            await keyless.PostHandleAsync((ICommand)new SampleCommand("x"));
            await keyless.HandleFailureAsync((ICommand)new SampleCommand("x"), new Exception());
        }

        [Fact]
        public async Task AddRequestConstraints_registers_behavior_as_hooks()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()))).As<IDistributedCache>();
            builder.AddRequestConstraints(_ => new[] { "k" }, (_, v) => v < 1, _ => null);
            await using var container = builder.Build();

            Assert.IsType<RequestExecutionConstraintBehavior>(container.Resolve<IPreHandleCommand>());
            Assert.IsType<RequestExecutionConstraintBehavior>(container.Resolve<IHandleQueryFailure>());
        }

        [Fact]
        public void Command_constraint_exception_is_business_exception()
        {
            Assert.IsAssignableFrom<ServiceComponents.Core.Exceptions.BusinessException>(new CommandConstraintException());
            Assert.Equal("m", new CommandConstraintException("m").Message);
            Assert.NotNull(new CommandConstraintException("m", new Exception()).InnerException);
        }
    }
}
