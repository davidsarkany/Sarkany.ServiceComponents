using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Mediator;

namespace ServiceComponents.Infrastructure.Dispatchers
{
    // Strongly typed bridges between the runtime type of a message and its handler and hook interfaces.
    // One instance is created per message type and cached, replacing the former 'dynamic' dispatch:
    // no runtime binder, and handlers or hooks do not need to be public.

    internal abstract class CommandInvoker
    {
        private static readonly ConcurrentDictionary<Type, CommandInvoker> Cache = new();

        public static CommandInvoker For(ICommand command) =>
            Cache.GetOrAdd(command.GetType(), type => (CommandInvoker)Activator.CreateInstance(typeof(CommandInvoker<>).MakeGenericType(type)));

        public abstract object ResolveHandler(IComponentContext context);
        public abstract Task HandleAsync(object handler, ICommand command, CancellationToken cancellationToken);
        public abstract Task PreHandleAsync(IComponentContext context, ICommand command, CancellationToken cancellationToken);
        public abstract Task PostHandleAsync(IComponentContext context, ICommand command, CancellationToken cancellationToken);
        public abstract Task HandleFailureAsync(IComponentContext context, ICommand command, Exception exception, CancellationToken cancellationToken);
    }

    internal sealed class CommandInvoker<T> : CommandInvoker where T : ICommand
    {
        public override object ResolveHandler(IComponentContext context) => context.ResolveOptional<IHandleCommand<T>>();

        public override Task HandleAsync(object handler, ICommand command, CancellationToken cancellationToken) =>
            ((IHandleCommand<T>)handler).HandleAsync((T)command, cancellationToken);

        public override async Task PreHandleAsync(IComponentContext context, ICommand command, CancellationToken cancellationToken)
        {
            foreach (var hook in context.Resolve<IEnumerable<IPreHandleCommand<T>>>()) {
                await hook.PreHandleAsync((T)command, cancellationToken);
            }
        }

        public override async Task PostHandleAsync(IComponentContext context, ICommand command, CancellationToken cancellationToken)
        {
            foreach (var hook in context.Resolve<IEnumerable<IPostHandleCommand<T>>>()) {
                await hook.PostHandleAsync((T)command, cancellationToken);
            }
        }

        public override async Task HandleFailureAsync(IComponentContext context, ICommand command, Exception exception, CancellationToken cancellationToken)
        {
            foreach (var hook in context.Resolve<IEnumerable<IHandleCommandFailure<T>>>()) {
                await hook.HandleFailureAsync((T)command, exception, cancellationToken);
            }
        }
    }

    internal abstract class QueryInvoker<TResult>
    {
        private static readonly ConcurrentDictionary<Type, QueryInvoker<TResult>> Cache = new();

        public static QueryInvoker<TResult> For(IQuery<TResult> query) =>
            Cache.GetOrAdd(query.GetType(), type => (QueryInvoker<TResult>)Activator.CreateInstance(typeof(QueryInvoker<,>).MakeGenericType(type, typeof(TResult))));

        public abstract object ResolveHandler(IComponentContext context);
        public abstract Task<TResult> HandleAsync(object handler, IQuery<TResult> query, CancellationToken cancellationToken);
        public abstract Task PreHandleAsync(IComponentContext context, IQuery<TResult> query, CancellationToken cancellationToken);
        public abstract Task PostHandleAsync(IComponentContext context, IQuery<TResult> query, TResult result, CancellationToken cancellationToken);
        public abstract Task HandleFailureAsync(IComponentContext context, IQuery<TResult> query, Exception exception, CancellationToken cancellationToken);
    }

    internal sealed class QueryInvoker<TQuery, TResult> : QueryInvoker<TResult> where TQuery : IQuery<TResult>
    {
        public override object ResolveHandler(IComponentContext context) => context.ResolveOptional<IHandleQuery<TQuery, TResult>>();

        public override Task<TResult> HandleAsync(object handler, IQuery<TResult> query, CancellationToken cancellationToken) =>
            ((IHandleQuery<TQuery, TResult>)handler).HandleAsync((TQuery)query, cancellationToken);

        public override async Task PreHandleAsync(IComponentContext context, IQuery<TResult> query, CancellationToken cancellationToken)
        {
            foreach (var hook in context.Resolve<IEnumerable<IPreHandleQuery<TQuery, TResult>>>()) {
                await hook.PreHandleAsync((TQuery)query, cancellationToken);
            }
        }

        // The value returned by the generic post hooks is ignored, the handler's result is returned to the caller.
        public override async Task PostHandleAsync(IComponentContext context, IQuery<TResult> query, TResult result, CancellationToken cancellationToken)
        {
            foreach (var hook in context.Resolve<IEnumerable<IPostHandleQuery<TQuery, TResult>>>()) {
                await hook.PostHandleAsync((TQuery)query, result, cancellationToken);
            }
        }

        public override async Task HandleFailureAsync(IComponentContext context, IQuery<TResult> query, Exception exception, CancellationToken cancellationToken)
        {
            foreach (var hook in context.Resolve<IEnumerable<IHandleQueryFailure<TQuery, TResult>>>()) {
                await hook.HandleFailureAsync((TQuery)query, exception, cancellationToken);
            }
        }
    }

    internal abstract class EventInvoker
    {
        private static readonly ConcurrentDictionary<Type, EventInvoker> Cache = new();

        public static EventInvoker For(IEvent @event) =>
            Cache.GetOrAdd(@event.GetType(), type => (EventInvoker)Activator.CreateInstance(typeof(EventInvoker<>).MakeGenericType(type)));

        public abstract IEnumerable<object> ResolveHandlers(IComponentContext context);
        public abstract Task HandleAsync(object handler, IEvent @event, CancellationToken cancellationToken);
        public abstract Task PreHandleAsync(IComponentContext context, IEvent @event, CancellationToken cancellationToken);
        public abstract Task PostHandleAsync(IComponentContext context, IEvent @event, CancellationToken cancellationToken);
        public abstract Task HandleFailureAsync(IComponentContext context, IEvent @event, Exception exception, CancellationToken cancellationToken);
    }

    internal sealed class EventInvoker<T> : EventInvoker where T : IEvent
    {
        public override IEnumerable<object> ResolveHandlers(IComponentContext context) => context.Resolve<IEnumerable<IHandleEvent<T>>>();

        public override Task HandleAsync(object handler, IEvent @event, CancellationToken cancellationToken) =>
            ((IHandleEvent<T>)handler).HandleAsync((T)@event, cancellationToken);

        public override async Task PreHandleAsync(IComponentContext context, IEvent @event, CancellationToken cancellationToken)
        {
            foreach (var hook in context.Resolve<IEnumerable<IPreHandleEvent<T>>>()) {
                await hook.PreHandleAsync((T)@event, cancellationToken);
            }
        }

        public override async Task PostHandleAsync(IComponentContext context, IEvent @event, CancellationToken cancellationToken)
        {
            foreach (var hook in context.Resolve<IEnumerable<IPostHandleEvent<T>>>()) {
                await hook.PostHandleAsync((T)@event, cancellationToken);
            }
        }

        public override async Task HandleFailureAsync(IComponentContext context, IEvent @event, Exception exception, CancellationToken cancellationToken)
        {
            foreach (var hook in context.Resolve<IEnumerable<IHandleEventFailure<T>>>()) {
                await hook.HandleFailureAsync((T)@event, exception, cancellationToken);
            }
        }
    }
}
