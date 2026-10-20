using System.Threading;
using System.Threading.Tasks;
using Autofac;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Mediator;

namespace ServiceComponents.Infrastructure.Validation
{
    /// <summary>
    /// Runs the FluentValidation validators of the command's runtime type and throws <c>ValidationException</c> on failures.
    /// </summary>
    public class CommandValidationBehavior<T> : IPreHandleCommand<T> where T : ICommand
    {
        private readonly ILifetimeScope _scope;

        public CommandValidationBehavior(ILifetimeScope scope)
        {
            _scope = scope;
        }

        public Task PreHandleAsync(T command, CancellationToken cancellationToken = default)
        {
            return ValidationInvoker.For(command.GetType()).ValidateAsync(_scope, command, cancellationToken);
        }
    }
}
