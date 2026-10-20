using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Moq;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application;
using ServiceComponents.Application.Mediator;
using ServiceComponents.Core.Services;
using ServiceComponents.Infrastructure.CorrelationContext;
using ServiceComponents.Infrastructure.EventRecorder;
using Xunit;

namespace ServiceComponents.UnitTests.Infrastructure
{
    public class EventRecorderTests
    {
        private readonly Mock<IClock> _clock = new Mock<IClock>();
        private DateTime _now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        private readonly ICorrelation _correlation = new Correlation { CorrelationId = "corr" };

        public EventRecorderTests()
        {
            _clock.SetupGet(c => c.UtcNow).Returns(() => _now);
        }

        private EventRecorderService Recorder(int ttl = 60000) => new EventRecorderService(new LoggerConfiguration().CreateLogger(), _clock.Object, ttl);

        private static Task Feed(EventRecorderService recorder, SampleEvent @event, ICorrelation correlation) =>
            new EventRecorderFeeder(recorder, correlation).PreHandleAsync(@event);

        [Fact]
        public async Task Already_recorded_event_is_returned_immediately()
        {
            var recorder = Recorder();
            var @event = new SampleEvent("a");
            await Feed(recorder, @event, _correlation);

            var result = await recorder.WaitFor<SampleEvent>((e, c) => e.Data == "a" && c.CorrelationId == "corr", TimeSpan.FromSeconds(5));

            Assert.Same(@event, result);
            var recorded = Assert.Single(recorder.RecordedEvents);
            Assert.Equal(_now, recorded.TimeStamp);
            Assert.Same(_correlation, recorded.Correlation);
        }

        [Fact]
        public async Task Waiter_is_released_by_later_matching_event()
        {
            var recorder = Recorder();
            var wait = recorder.WaitFor<SampleEvent>((e, c) => e.Data == "b");

            await Feed(recorder, new SampleEvent("other"), _correlation);
            Assert.False(wait.IsCompleted);
            var @event = new SampleEvent("b");
            await Feed(recorder, @event, _correlation);

            Assert.Same(@event, await wait.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public async Task Timeout_current_behavior_returns_default()
        {
            var recorder = Recorder();

            // Current behavior: cancellation releases the wait handle and the task completes with default instead of failing.
            var result = await recorder.WaitFor<SampleEvent>((e, c) => true, TimeSpan.FromMilliseconds(50)).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Null(result);
        }

        [Fact]
        public async Task Expired_events_are_removed()
        {
            var recorder = Recorder(ttl: 1000);
            await Feed(recorder, new SampleEvent("old"), _correlation);

            _now = _now.AddSeconds(2);
            var wait = recorder.WaitFor<SampleEvent>((e, c) => e.Data == "old", TimeSpan.FromMilliseconds(50));

            Assert.Empty(recorder.RecordedEvents);
            Assert.Null(await wait.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public async Task Clear_removes_recorded_events()
        {
            var recorder = Recorder();
            await Feed(recorder, new SampleEvent("a"), _correlation);

            await recorder.ClearRecordedEvents();

            Assert.Empty(recorder.RecordedEvents);
        }

        [Fact]
        public async Task Already_cancelled_token_yields_cancelled_task_without_leaking_a_waiter()
        {
            var recorder = Recorder();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recorder.WaitFor<SampleEvent>((_, _) => true, new CancellationToken(true)));

            // The next event must not be consumed by an abandoned waiter: a later wait still finds it.
            await Feed(recorder, new SampleEvent("later"), _correlation);
            Assert.Equal("later", (await recorder.WaitFor<SampleEvent>((_, _) => true, TimeSpan.FromSeconds(1))).Data);
        }

        [Fact]
        public async Task Waiter_for_a_base_type_receives_live_events()
        {
            var recorder = Recorder();
            var wait = recorder.WaitFor<IEvent>((_, _) => true, TimeSpan.FromSeconds(10));

            // Fixed in 0.3 (known issue #12): live events used to be matched by exact type only.
            var @event = new SampleEvent("live");
            await Feed(recorder, @event, _correlation);

            Assert.Same(@event, await wait.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public async Task Event_recorded_while_registering_is_not_missed()
        {
            var recorder = Recorder();

            // Many interleavings of WaitFor and recording: each waiter must complete with its event.
            var tasks = Enumerable.Range(0, 200).Select(async i => {
                var wait = recorder.WaitFor<SampleEvent>((e, _) => e.Data == $"e{i}", TimeSpan.FromSeconds(10));
                await Task.Yield();
                await Feed(recorder, new SampleEvent($"e{i}"), _correlation);
                return (await wait)?.Data;
            });

            var results = await Task.WhenAll(tasks);
            Assert.Equal(Enumerable.Range(0, 200).Select(i => $"e{i}"), results);
        }

        [Fact]
        public void UseEventRecorder_registers_singleton_service_and_feeder_hook()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance(_clock.Object);
            builder.AddCorrelationInfo();
            builder.UseEventRecorder();
            using var container = builder.Build();

            Assert.Same(container.Resolve<EventRecorderService>(), container.Resolve<IEventRecorder>());
            Assert.IsType<EventRecorderFeeder>(container.Resolve<IPreHandleEvent>());
        }
    }
}
