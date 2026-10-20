using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ServiceComponents.Api.Mediator;

namespace ServiceComponents.Domain
{
    /// <summary>
    /// Aggregate root whose state is built by applying events. Every event type needs a non-public
    /// <c>Apply(TEvent)</c> method on the aggregate or one of its base classes.
    /// </summary>
    public abstract class EventSourcedAggregateRoot : IEventSourceAggregateRoot
    {
        private static readonly ConcurrentDictionary<(Type aggregate, Type @event), MethodInfo> ApplyMethods = new();

        public string AggregateId { get; protected set; }
        public long Version { get; protected set; }

        private readonly ICollection<IEvent> _uncommittedEvents = new List<IEvent>();

        protected EventSourcedAggregateRoot(string aggregateId)
        {
            if (string.IsNullOrEmpty(aggregateId)) {
                throw new ArgumentNullException(nameof(aggregateId));
            }

            AggregateId = aggregateId;
        }

        protected EventSourcedAggregateRoot(IEnumerable<IEvent> events)
        {
            if (events == null) {
                throw new ArgumentException(nameof(events));
            }

            foreach (var @event in events) {
                ApplyEvent(@event);
            }
        }

        protected void RaiseEvent(IEvent @event)
        {
            var apply = FindApply(@event);
            _uncommittedEvents.Add(@event);
            Apply(apply, @event);
        }

        protected void ApplyEvent(IEvent @event)
        {
            Apply(FindApply(@event), @event);
        }

        // The version is incremented before Apply runs, so Apply methods observe the new version.
        // Exceptions thrown by an Apply method surface wrapped in TargetInvocationException.
        private void Apply(MethodInfo apply, IEvent @event)
        {
            Version++;
            apply.Invoke(this, new object[] { @event });
        }

        private MethodInfo FindApply(IEvent @event)
        {
            var method = ApplyMethods.GetOrAdd((GetType(), @event.GetType()), key => FindApplyMethod(key.aggregate, key.@event));
            return method ?? throw new InvalidOperationException($"No handler for event {@event.GetType().FullName} found on aggregate {GetType().FullName}");
        }

        private static MethodInfo FindApplyMethod(Type aggregateType, Type eventType)
        {
            for (var type = aggregateType; type != null && type != typeof(EventSourcedAggregateRoot); type = type.BaseType) {
                var method = type
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .SingleOrDefault(info => info.Name == "Apply" && info.GetParameters().Length == 1 && info.GetParameters()[0].ParameterType == eventType);
                if (method != null) {
                    return method;
                }
            }

            return null;
        }

        ICollection<IEvent> IEventSourceAggregateRoot.GetUncommittedEvents() => _uncommittedEvents;
        void IEventSourceAggregateRoot.ClearUncommittedEvents() => _uncommittedEvents.Clear();
    }
}
