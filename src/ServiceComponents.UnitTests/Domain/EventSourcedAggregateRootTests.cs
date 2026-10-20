using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Domain;
using Xunit;

namespace ServiceComponents.UnitTests.Domain
{
    public class EventSourcedAggregateRootTests
    {
        private class Counter : EventSourcedAggregateRoot
        {
            public int Value;

            public Counter(string id) : base(id) { }
            public Counter(System.Collections.Generic.IEnumerable<IEvent> events) : base(events) { }

            public void Increment() => RaiseEvent(new SampleEvent("inc"));
            public void Unknown() => RaiseEvent(new UnhandledEvent());

            private void Apply(SampleEvent @event) => Value++;
        }

        private class UnhandledEvent : Event
        {
            public UnhandledEvent() : base(default) { }
        }

        [Fact]
        public void Constructor_requires_aggregate_id()
        {
            Assert.Throws<ArgumentNullException>(() => new Counter((string)null));
            Assert.Throws<ArgumentNullException>(() => new Counter(string.Empty));
        }

        [Fact]
        public void Constructor_requires_events()
        {
            Assert.Throws<ArgumentException>(() => new Counter((System.Collections.Generic.IEnumerable<IEvent>)null));
        }

        [Fact]
        public void Raised_events_are_applied_versioned_and_uncommitted()
        {
            var counter = new Counter("id");
            counter.Increment();
            counter.Increment();

            var root = (IEventSourceAggregateRoot)counter;
            Assert.Equal("id", counter.AggregateId);
            Assert.Equal(2, counter.Value);
            Assert.Equal(2, counter.Version);
            Assert.Equal(2, root.GetUncommittedEvents().Count);

            root.ClearUncommittedEvents();
            Assert.Empty(root.GetUncommittedEvents());
        }

        [Fact]
        public void Replayed_events_are_not_uncommitted()
        {
            var counter = new Counter(new IEvent[] { new SampleEvent("a"), new SampleEvent("b") });

            Assert.Equal(2, counter.Value);
            Assert.Equal(2, counter.Version);
            Assert.Null(counter.AggregateId);
            Assert.Empty(((IEventSourceAggregateRoot)counter).GetUncommittedEvents());
        }

        [Fact]
        public void Missing_apply_method_throws_without_changing_the_aggregate()
        {
            var counter = new Counter("id");

            var error = Assert.Throws<InvalidOperationException>(() => counter.Unknown());

            // Fixed (known issue #15): version and uncommitted events used to be updated before the lookup failed.
            Assert.Contains("No handler for event", error.Message);
            Assert.Equal(0, counter.Version);
            Assert.Empty(((IEventSourceAggregateRoot)counter).GetUncommittedEvents());
        }

        [Fact]
        public void Apply_sees_the_new_version_and_its_exceptions_are_wrapped()
        {
            var aggregate = new Failing("id");

            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => aggregate.Fail());

            Assert.IsType<ApplicationException>(error.InnerException);
            Assert.Equal(1, aggregate.VersionSeenByApply);
            Assert.Equal(1, aggregate.Version);
        }

        public abstract class BaseAggregate : EventSourcedAggregateRoot
        {
            public bool Applied;
            protected BaseAggregate(string id) : base(id) { }
            public void Raise() => RaiseEvent(new SampleEvent("x"));
            private void Apply(SampleEvent @event) => Applied = true;
        }

        public class DerivedAggregate : BaseAggregate
        {
            public DerivedAggregate() : base("id") { }
        }

        [Fact]
        public void Private_apply_methods_of_base_classes_are_found()
        {
            var aggregate = new DerivedAggregate();

            // Fixed (known issue #15): only the most derived type used to be searched.
            aggregate.Raise();

            Assert.True(aggregate.Applied);
            Assert.Equal(1, aggregate.Version);
        }

        private class Failing : EventSourcedAggregateRoot
        {
            public long VersionSeenByApply;

            public Failing(string id) : base(id) { }

            public void Fail() => RaiseEvent(new SampleEvent("x"));

            private void Apply(SampleEvent @event)
            {
                VersionSeenByApply = Version;
                throw new ApplicationException("apply failed");
            }
        }

        [Fact]
        public async Task Execute_loads_runs_and_saves()
        {
            var aggregate = new Counter("id");
            var repository = new Mock<IEventSourceAggregateRepository>();
            var token = new CancellationTokenSource().Token;
            repository.Setup(r => r.GetByIdAsync("id", token)).ReturnsAsync(aggregate);

            await repository.Object.Execute<Counter>("id", a => ((Counter)a).Increment(), token);

            Assert.Equal(1, aggregate.Value);
            repository.Verify(r => r.SaveAsync(aggregate, token), Times.Once);
        }
    }
}
