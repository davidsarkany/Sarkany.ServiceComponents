using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application;
using ServiceComponents.Core.Services;

namespace ServiceComponents.Infrastructure.EventRecorder
{
    /// <summary>
    /// Records handled events for a limited time (<c>eventTTL</c> milliseconds) and lets callers wait for one.
    /// </summary>
    /// <remarks>
    /// <see cref="WaitFor{T}(Func{T, ICorrelation, bool}, CancellationToken)"/> returns a recorded matching event
    /// immediately, otherwise the next matching event. When the wait times out or is cancelled the task completes with
    /// <c>default</c>; a token that is already cancelled yields a cancelled task.
    /// </remarks>
    public class EventRecorderService : IEventRecorder
    {
        private readonly ILogger _log;
        private readonly IClock _clock;
        private readonly int _eventTtl;

        private readonly object _lockObj = new();
        private readonly List<RecordedEvent> _events = new();
        private readonly List<Waiter> _waiters = new();

        public EventRecorderService(ILogger log, IClock clock, int eventTTL = 60000)
        {
            _log = log;
            _clock = clock;
            _eventTtl = eventTTL;
        }

        public async Task<T> WaitFor<T>(Func<T, ICorrelation, bool> filter, TimeSpan timeOut) where T : IEvent
        {
            using var cancellationTokenSource = new CancellationTokenSource(timeOut);
            return await WaitFor(filter, cancellationTokenSource.Token);
        }

        public Task<T> WaitFor<T>(Func<T, ICorrelation, bool> filter, CancellationToken cancellationToken = default) where T : IEvent
        {
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled<T>(cancellationToken);
            }

            Waiter waiter;
            lock (_lockObj) {
                RemoveExpiredEvents();

                var recorded = _events.FirstOrDefault(x => x.Event is T @event && filter(@event, x.Correlation));
                if (recorded != null) {
                    _log.Debug("Matching event for {eventType} returned from event store with age of {eventAge} ms", typeof(T).FullName, (_clock.UtcNow - recorded.TimeStamp).TotalMilliseconds);
                    return Task.FromResult((T)recorded.Event);
                }

                // Registered under the same lock as the lookup, so an event recorded in between cannot be missed.
                waiter = new Waiter((@event, correlation) => @event is T typed && filter(typed, correlation));
                _waiters.Add(waiter);
            }

            _log.Debug("Creating event awaiter task {awaiterTaskId} for event {eventType}", waiter.GetHashCode(), typeof(T).FullName);
            var registration = cancellationToken.Register(() => {
                _log.Debug("Awaiting event on task {awaiterTaskId} cancelled for event {eventType}", waiter.GetHashCode(), typeof(T).FullName);
                lock (_lockObj) {
                    _waiters.Remove(waiter);
                }

                waiter.Completion.TrySetResult(default);
            });

            return Await<T>(waiter, registration);
        }

        private static async Task<T> Await<T>(Waiter waiter, CancellationTokenRegistration registration)
        {
            await using (registration) {
                var @event = await waiter.Completion.Task;
                return @event is T typed ? typed : default;
            }
        }

        internal Task PreHandleEvent(IEvent @event, ICorrelation correlation, CancellationToken cancellationToken = default)
        {
            List<Waiter> matched;
            lock (_lockObj) {
                RemoveExpiredEvents();
                _log.Debug("Recording event {eventType} in event store", @event.GetType().FullName);
                _events.Add(new RecordedEvent(_clock.UtcNow, @event, correlation));

                _log.Verbose("Checking {awaiterCount} awaiter(s)", _waiters.Count);
                matched = _waiters.Where(waiter => waiter.Filter(@event, correlation)).ToList();
                _waiters.RemoveAll(matched.Contains);
            }

            foreach (var waiter in matched) {
                _log.Verbose("Event awaiter task {awaiterTaskId} matching event {eventType}", waiter.GetHashCode(), @event.GetType().FullName);
                waiter.Completion.TrySetResult(@event);
            }

            return Task.CompletedTask;
        }

        public IEnumerable<RecordedEvent> RecordedEvents {
            get {
                lock (_lockObj) {
                    return _events.ToArray();
                }
            }
        }

        public Task ClearRecordedEvents(CancellationToken cancellationToken = default)
        {
            lock (_lockObj) {
                _events.Clear();
            }

            return Task.CompletedTask;
        }

        // Callers hold _lockObj.
        private void RemoveExpiredEvents()
        {
            var maxTime = _clock.UtcNow.AddMilliseconds(-_eventTtl);
            _events.RemoveAll(recordedEvent => {
                if (recordedEvent.TimeStamp >= maxTime) {
                    return false;
                }

                _log.Debug("Removing expired event {eventType} with age of {eventAge} from event store", recordedEvent.Event.GetType().FullName, (_clock.UtcNow - recordedEvent.TimeStamp).TotalMilliseconds);
                return true;
            });
        }

        private sealed class Waiter
        {
            public Func<IEvent, ICorrelation, bool> Filter { get; }
            public TaskCompletionSource<IEvent> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Waiter(Func<IEvent, ICorrelation, bool> filter)
            {
                Filter = filter;
            }
        }
    }
}
