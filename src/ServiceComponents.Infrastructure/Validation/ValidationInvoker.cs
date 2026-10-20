using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentValidation;

namespace ServiceComponents.Infrastructure.Validation
{
    // Typed access to the validators of a message's runtime type, cached per type (replaces the former 'dynamic' call).
    internal abstract class ValidationInvoker
    {
        private static readonly ConcurrentDictionary<Type, ValidationInvoker> Cache = new();

        public static ValidationInvoker For(Type messageType) =>
            Cache.GetOrAdd(messageType, type => (ValidationInvoker)Activator.CreateInstance(typeof(ValidationInvoker<>).MakeGenericType(type)));

        public abstract Task ValidateAsync(IComponentContext context, object message, CancellationToken cancellationToken);
    }

    internal sealed class ValidationInvoker<T> : ValidationInvoker
    {
        public override async Task ValidateAsync(IComponentContext context, object message, CancellationToken cancellationToken)
        {
            foreach (var validator in context.Resolve<IEnumerable<IValidator<T>>>()) {
                await validator.ValidateAndThrowAsync((T)message, cancellationToken);
            }
        }
    }
}
