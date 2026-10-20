using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Mediator;

namespace ServiceComponents.Infrastructure.Behaviors.CommandConstraints
{
    /// <summary>
    /// Limits how many requests may run at the same time per key. Before a request the counter of each of its keys is
    /// incremented if <c>constraint(key, current)</c> allows it, otherwise <see cref="CommandConstraintException"/> is thrown;
    /// the counters are decremented when the request completes or fails. Counters live in <see cref="IDistributedCache"/>
    /// as little-endian Int32 values.
    /// </summary>
    public class RequestExecutionConstraintBehavior :
        IPreHandleCommand, IPostHandleCommand, IHandleCommandFailure,
        IPreHandleQuery, IPostHandleQuery, IHandleQueryFailure
    {
        // Marks a CommandConstraintException thrown for a request, so that only that request skips the decrement and
        // the requests it is nested in still release their own counters.
        private const string RejectedRequestKey = "ServiceComponents.RequestExecutionConstraint.RejectedRequest";

        private readonly IDistributedCache _cache;
        private readonly Func<IRequest, string[]> _keys;
        private readonly Func<string, int, bool> _constraint;
        private readonly Func<string, TimeSpan?> _expiry;

        public RequestExecutionConstraintBehavior(IDistributedCache cache, Func<IRequest, string[]> keys, Func<string, int, bool> constraint, Func<string, TimeSpan?> expiry)
        {
            _cache = cache;
            _keys = keys;
            _constraint = constraint;
            _expiry = expiry;
        }

        public Task PreHandleAsync(ICommand command, CancellationToken cancellationToken = default) => Acquire(command, command.CommandId, cancellationToken);

        public Task PostHandleAsync(ICommand command, CancellationToken cancellationToken = default) => Release(command, cancellationToken);

        public Task HandleFailureAsync(ICommand command, Exception exception, CancellationToken cancellationToken = default) => ReleaseOnFailure(command, command.CommandId, exception);

        public Task PreHandleAsync(IQuery query, CancellationToken cancellationToken = default) => Acquire(query, query.QueryId, cancellationToken);

        public Task PostHandleAsync(IQuery query, object result, CancellationToken cancellationToken = default) => Release(query, cancellationToken);

        public Task HandleFailureAsync(IQuery query, Exception exception, CancellationToken cancellationToken = default) => ReleaseOnFailure(query, query.QueryId, exception);

        private async Task Acquire(IRequest request, string requestId, CancellationToken cancellationToken)
        {
            var keys = _keys(request);
            if (keys == null || keys.Length == 0) {
                return;
            }

            var acquired = new List<string>(keys.Length);
            foreach (var key in keys) {
                var value = await Read(key, cancellationToken) ?? 0;
                if (!_constraint(key, value)) {
                    // Give back what this request already took before rejecting it.
                    foreach (var taken in acquired) {
                        await Decrement(taken, CancellationToken.None);
                    }

                    var exception = new CommandConstraintException("Cannot execute request because of constraints");
                    exception.Data[RejectedRequestKey] = requestId;
                    throw exception;
                }

                await Write(key, value + 1, cancellationToken);
                acquired.Add(key);
            }
        }

        private async Task Release(IRequest request, CancellationToken cancellationToken)
        {
            var keys = _keys(request);
            if (keys == null) {
                return;
            }

            foreach (var key in keys) {
                await Decrement(key, cancellationToken);
            }
        }

        private async Task ReleaseOnFailure(IRequest request, string requestId, Exception exception)
        {
            if (exception is CommandConstraintException && Equals(exception.Data[RejectedRequestKey], requestId)) {
                return;
            }

            // Cleanup must not depend on the (possibly cancelled) request token, and must not hide the original exception.
            try {
                await Release(request, CancellationToken.None);
            }
            catch {
                // The counters expire with the configured expiry.
            }
        }

        private async Task Decrement(string key, CancellationToken cancellationToken)
        {
            var value = await Read(key, cancellationToken);
            if (value == null) {
                return;
            }

            if (value.Value <= 1) {
                await _cache.RemoveAsync(key, cancellationToken);
            }
            else {
                await Write(key, value.Value - 1, cancellationToken);
            }
        }

        private async Task<int?> Read(string key, CancellationToken cancellationToken)
        {
            var bytes = await _cache.GetAsync(key, cancellationToken);
            return bytes == null ? null : BitConverter.ToInt32(bytes, 0);
        }

        private Task Write(string key, int value, CancellationToken cancellationToken)
        {
            return _cache.SetAsync(key, BitConverter.GetBytes(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _expiry(key) }, cancellationToken);
        }
    }
}
