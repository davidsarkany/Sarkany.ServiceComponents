using System.Threading;
using System.Threading.Tasks;
using Autofac;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Mediator;

namespace ServiceComponents.Infrastructure.Validation
{
    /// <summary>
    /// Runs the FluentValidation validators of the query's runtime type and throws <c>ValidationException</c> on failures.
    /// </summary>
    public class QueryValidationBehavior<TQuery, TResult> : IPreHandleQuery<TQuery, TResult> where TQuery : IQuery<TResult>
    {
        private readonly ILifetimeScope _scope;

        public QueryValidationBehavior(ILifetimeScope scope)
        {
            _scope = scope;
        }

        public Task PreHandleAsync(TQuery query, CancellationToken cancellationToken = default)
        {
            return ValidationInvoker.For(query.GetType()).ValidateAsync(_scope, query, cancellationToken);
        }
    }
}
