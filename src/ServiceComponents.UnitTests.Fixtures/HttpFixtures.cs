using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application;
using ServiceComponents.Application.Mediator;
using ServiceComponents.AspNet.Http;
using ServiceComponents.Core.Exceptions;

namespace ServiceComponents.UnitTests.Fixtures
{
    public class HttpCommand : Command
    {
        public string Mode { get; }

        public HttpCommand(string mode, string commandId = default) : base(commandId)
        {
            Mode = mode;
        }
    }

    public class HttpCommandValidator : AbstractValidator<HttpCommand>
    {
        public HttpCommandValidator() => RuleFor(c => c.Mode).NotEmpty().WithErrorCode("REQUIRED").WithMessage("Mode is required");
    }

    public class HttpQuery : Query<HttpQueryResult>
    {
        public int Value { get; }

        public HttpQuery(int value, string queryId = default) : base(queryId)
        {
            Value = value;
        }
    }

    public class HttpQueryResult
    {
        public int Doubled { get; set; }
        public string Text { get; set; }
    }

    public class HttpEvent : Event
    {
        public string Data { get; }

        public HttpEvent(string data, string eventId = default) : base(eventId)
        {
            Data = data;
        }
    }

    public class ReceivedRequests
    {
        public ConcurrentQueue<(string what, string correlationId, string causationId, string currentId)> Items { get; } = new();
    }

    public class HttpHandlers : IHandleCommand<HttpCommand>, IHandleQuery<HttpQuery, HttpQueryResult>, IHandleEvent<HttpEvent>
    {
        private readonly ReceivedRequests _received;
        private readonly ICorrelation _correlation;

        public HttpHandlers(ReceivedRequests received, ICorrelation correlation)
        {
            _received = received;
            _correlation = correlation;
        }

        public Task HandleAsync(HttpCommand command, CancellationToken cancellationToken = default)
        {
            _received.Items.Enqueue(("command:" + command.Mode, _correlation.CorrelationId, _correlation.CausationId, _correlation.CurrentId));
            return command.Mode switch {
                "business" => throw new BusinessException(42, "Business rule violated"),
                "notfound" => throw new NotFoundException("Thing not found"),
                "conflict" => throw new ConflictException("Conflict!"),
                "unauthorized" => throw new UnauthorizedAccessException("No access"),
                "notimplemented" => throw new NotImplementedException("Later"),
                "invalid" => throw new InvalidOperationException("Invalid state"),
                "crash" => throw new ApplicationException("secret details"),
                _ => Task.CompletedTask
            };
        }

        public Task<HttpQueryResult> HandleAsync(HttpQuery query, CancellationToken cancellationToken = default)
        {
            _received.Items.Enqueue(("query:" + query.Value, _correlation.CorrelationId, _correlation.CausationId, _correlation.CurrentId));
            return Task.FromResult(query.Value == 0 ? null : new HttpQueryResult { Doubled = query.Value * 2, Text = "v" + query.Value });
        }

        public Task HandleAsync(HttpEvent @event, CancellationToken cancellationToken = default)
        {
            _received.Items.Enqueue(("event:" + @event.Data, _correlation.CorrelationId, _correlation.CausationId, _correlation.CurrentId));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// The generic endpoint as consumer applications implement it (see ReferenceApplication2.AspNet).
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class GenericController : ControllerBase
    {
        private readonly HttpRequestParser _parser;
        private readonly IReceiveHttpCommand _commandReceiver;
        private readonly IReceiveHttpQuery _queryReceiver;
        private readonly IReceiveHttpEvent _eventReceiver;
        private readonly ReceivedRequests _received;

        public GenericController(HttpRequestParser parser, IReceiveHttpCommand commandReceiver, IReceiveHttpQuery queryReceiver, IReceiveHttpEvent eventReceiver, ReceivedRequests received)
        {
            _parser = parser;
            _commandReceiver = commandReceiver;
            _queryReceiver = queryReceiver;
            _eventReceiver = eventReceiver;
            _received = received;
        }

        [HttpPost]
        public async Task<IActionResult> Post(CancellationToken cancellationToken)
        {
            var headers = string.Join("|", Request.Headers
                .Where(h => h.Key is "domain-type" or "correlation-id" or "causation-id" or "Content-Type" or "Accept")
                .OrderBy(h => h.Key)
                .Select(h => $"{h.Key}={h.Value}"));
            _received.Items.Enqueue(("headers:" + headers, null, null, null));

            object result = null;
            await _parser.Parse(Request,
                async (command, ct) => await _commandReceiver.ReceiveAsync(command, ct),
                async (query, ct) => result = await _queryReceiver.ReceiveAsync((dynamic)query, ct),
                async (@event, ct) => await _eventReceiver.ReceiveAsync(@event, ct),
                cancellationToken);
            return result == null ? Ok() : Ok(result);
        }
    }
}

namespace ServiceComponents.UnitTests.Fixtures
{
    using System.Security.Claims;
    using System.Text.Encodings.Web;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    public class SecuredCommand : Command
    {
        public SecuredCommand(string commandId = default) : base(commandId) { }
    }

    [Authorize(Roles = "admin")]
    public class SecuredCommandHandler : IHandleCommand<SecuredCommand>
    {
        private readonly ReceivedRequests _received;
        private readonly ICorrelation _correlation;

        public SecuredCommandHandler(ReceivedRequests received, ICorrelation correlation)
        {
            _received = received;
            _correlation = correlation;
        }

        public Task HandleAsync(SecuredCommand command, CancellationToken cancellationToken = default)
        {
            _received.Items.Enqueue(("secured", _correlation.CorrelationId, _correlation.CausationId, _correlation.CurrentId));
            return Task.CompletedTask;
        }
    }

    public class SecuredQuery : Query<string>
    {
        public SecuredQuery() : base(default) { }
    }

    [Authorize(Roles = "admin")]
    public class SecuredQueryHandler : IHandleQuery<SecuredQuery, string>
    {
        public Task<string> HandleAsync(SecuredQuery query, CancellationToken cancellationToken = default) => Task.FromResult("secret");
    }

    /// <summary>
    /// Authenticates requests carrying an "x-roles" header with those roles.
    /// </summary>
    public class HeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Header";

        public HeaderAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("x-roles", out var roles)) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(roles.ToString().Split(',').Select(role => new Claim(ClaimTypes.Role, role)).Append(new Claim(ClaimTypes.Name, "user")), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}

namespace ServiceComponents.UnitTests.Fixtures
{
    public class StringQuery : Query<string>
    {
        public StringQuery() : base(default) { }
    }

    public class StringQueryHandler : IHandleQuery<StringQuery, string>
    {
        public Task<string> HandleAsync(StringQuery query, CancellationToken cancellationToken = default) => Task.FromResult("plain value");
    }
}

namespace ServiceComponents.UnitTests.Fixtures
{
    public class FailingEvent : Event
    {
        public FailingEvent(string eventId = default) : base(eventId) { }
    }

    public class FailingEventHandler : IHandleEvent<FailingEvent>
    {
        private readonly ReceivedRequests _received;
        public FailingEventHandler(ReceivedRequests received) => _received = received;

        public Task HandleAsync(FailingEvent @event, CancellationToken cancellationToken = default)
        {
            _received.Items.Enqueue(("failing-event", null, null, @event.EventId));
            throw new InvalidOperationException("event handler failed");
        }
    }
}

namespace ServiceComponents.UnitTests.Fixtures
{
    public class FailingQuery : Query<string>
    {
        public FailingQuery() : base(default) { }
    }

    public class FailingQueryHandler : IHandleQuery<FailingQuery, string>
    {
        public Task<string> HandleAsync(FailingQuery query, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("query failed");
    }
}
