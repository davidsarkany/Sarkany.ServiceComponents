using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application;
using ServiceComponents.Application.Forwarder;
using ServiceComponents.Application.Mediator;
using ServiceComponents.Application.Monitoring;
using ServiceComponents.Application.Senders;
using Xunit;

#pragma warning disable CS0618 // Obsolete handler base classes are covered on purpose

namespace ServiceComponents.UnitTests.Application
{
    public class HandlerBaseTests
    {
        private readonly Mock<ISendCommand> _commandSender = new Mock<ISendCommand>();
        private readonly Mock<ISendQuery> _querySender = new Mock<ISendQuery>();
        private readonly Mock<IPublishEvent> _publisher = new Mock<IPublishEvent>();
        private readonly ICorrelation _correlation = Mock.Of<ICorrelation>();
        private readonly ILogger _log = Mock.Of<ILogger>();
        private readonly CancellationToken _token = new CancellationTokenSource().Token;

        public HandlerBaseTests()
        {
            _querySender.Setup(s => s.SendAsync(It.IsAny<SampleQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync("result");
        }

        private class CommandHandlerUnderTest : HandleCommand<SampleCommand>
        {
            public CommandHandlerUnderTest(ILogger log, ICorrelation correlation, ISendQuery querySender, IPublishEvent eventPublisher)
                : base(log, correlation, querySender, eventPublisher) { }

            public string QueryResult;
            public ILogger Logger => Log;
            public ICorrelation Corr => Correlation;

            public override async Task HandleAsync(SampleCommand command, CancellationToken cancellationToken = default)
            {
                QueryResult = await SendAsync(new SampleQuery(1), cancellationToken);
                await PublishAsync(new SampleEvent(command.Data), cancellationToken);
                await PublishAsync(new List<IEvent> { new SampleEvent("a"), new SampleEvent("b") }, cancellationToken);
            }
        }

        private class QueryHandlerUnderTest : HandleQuery<SampleQuery, string>
        {
            public QueryHandlerUnderTest(ILogger log, ICorrelation correlation, ISendQuery querySender) : base(log, correlation, querySender) { }

            public ILogger Logger => Log;
            public ICorrelation Corr => Correlation;

            public override async Task<string> HandleAsync(SampleQuery query, CancellationToken cancellationToken = default)
            {
                if (query.Number > 0) {
                    return "inner:" + await SendAsync(new SampleQuery(0), cancellationToken);
                }

                return "leaf";
            }
        }

        private class EventHandlerUnderTest : HandleEvent<SampleEvent>
        {
            public EventHandlerUnderTest(ILogger log, ICorrelation correlation, ISendCommand commandSender, ISendQuery querySender, IPublishEvent eventPublisher)
                : base(log, correlation, commandSender, querySender, eventPublisher) { }

            public string QueryResult;
            public ILogger Logger => Log;
            public ICorrelation Corr => Correlation;

            public override async Task HandleAsync(SampleEvent @event, CancellationToken cancellationToken = default)
            {
                await SendAsync(new SampleCommand(@event.Data), cancellationToken);
                QueryResult = await SendAsync(new SampleQuery(1), cancellationToken);
                await PublishAsync(new SampleEvent("next"), cancellationToken);
                await PublishAsync(new IEvent[] { new SampleEvent("c") }, cancellationToken);
            }
        }

        private class ObsoleteCommandHandler : CommandHandler<SampleCommand>
        {
            public ObsoleteCommandHandler(ILogger log, ICorrelation correlation, ISendQuery querySender, IPublishEvent eventPublisher)
                : base(log, correlation, querySender, eventPublisher) { }

            public string QueryResult;
            public ILogger Logger => Log;
            public ICorrelation Corr => Correlation;

            public override async Task HandleAsync(SampleCommand command, CancellationToken cancellationToken = default)
            {
                QueryResult = await SendAsync(new SampleQuery(1), cancellationToken);
                await PublishAsync(new SampleEvent(command.Data), cancellationToken);
                await PublishAsync(new IEvent[] { new SampleEvent("x") }, cancellationToken);
            }
        }

        private class ObsoleteQueryHandler : QueryHandler<SampleQuery, string>
        {
            public ObsoleteQueryHandler(ILogger log, ICorrelation correlation, ISendQuery querySender) : base(log, correlation, querySender) { }

            public ILogger Logger => Log;
            public ICorrelation Corr => Correlation;

            public override Task<string> HandleAsync(SampleQuery query, CancellationToken cancellationToken = default) => SendAsync(new SampleQuery(0), cancellationToken);
        }

        private class ObsoleteEventHandler : EventHandler<SampleEvent>
        {
            public ObsoleteEventHandler(ILogger log, ICorrelation correlation, ISendCommand commandSender, ISendQuery querySender)
                : base(log, correlation, commandSender, querySender) { }

            public string QueryResult;
            public ILogger Logger => Log;
            public ICorrelation Corr => Correlation;

            public override async Task HandleAsync(SampleEvent @event, CancellationToken cancellationToken = default)
            {
                await SendAsync(new SampleCommand(@event.Data), cancellationToken);
                QueryResult = await SendAsync(new SampleQuery(1), cancellationToken);
            }
        }

        private class Forwarder : CommandForwarder<SampleCommand, OtherCommand>
        {
            public readonly OtherCommand Output = new OtherCommand();

            public Forwarder(ISendCommand commandSender) : base(commandSender) { }

            public override Task<OtherCommand> Transform(SampleCommand input, CancellationToken cancellationToken) => Task.FromResult(Output);
        }

        [Fact]
        public async Task HandleCommand_helpers_delegate_to_senders()
        {
            var handler = new CommandHandlerUnderTest(_log, _correlation, _querySender.Object, _publisher.Object);

            await handler.HandleAsync(new SampleCommand("data"), _token);

            Assert.Equal("result", handler.QueryResult);
            Assert.Same(_log, handler.Logger);
            Assert.Same(_correlation, handler.Corr);
            _publisher.Verify(p => p.PublishAsync(It.Is<SampleEvent>(e => e.Data == "data"), _token), Times.Once);
            _publisher.Verify(p => p.PublishAsync(It.Is<IEnumerable<IEvent>>(e => e is List<IEvent>), _token), Times.Once);
        }

        [Fact]
        public async Task HandleQuery_helpers_delegate_to_query_sender()
        {
            var handler = new QueryHandlerUnderTest(_log, _correlation, _querySender.Object);

            Assert.Equal("inner:result", await handler.HandleAsync(new SampleQuery(1), _token));
            Assert.Equal("leaf", await handler.HandleAsync(new SampleQuery(0), _token));
            Assert.Same(_log, handler.Logger);
            Assert.Same(_correlation, handler.Corr);
            _querySender.Verify(s => s.SendAsync(It.IsAny<SampleQuery>(), _token), Times.Once);
        }

        [Fact]
        public async Task HandleEvent_helpers_delegate_to_senders()
        {
            var handler = new EventHandlerUnderTest(_log, _correlation, _commandSender.Object, _querySender.Object, _publisher.Object);

            await handler.HandleAsync(new SampleEvent("evt"), _token);

            Assert.Equal("result", handler.QueryResult);
            Assert.Same(_log, handler.Logger);
            Assert.Same(_correlation, handler.Corr);
            _commandSender.Verify(s => s.SendAsync(It.Is<SampleCommand>(c => c.Data == "evt"), _token), Times.Once);
            _publisher.Verify(p => p.PublishAsync(It.Is<SampleEvent>(e => e.Data == "next"), _token), Times.Once);
            _publisher.Verify(p => p.PublishAsync(It.IsAny<IEnumerable<IEvent>>(), _token), Times.Once);
        }

        [Fact]
        public async Task Obsolete_handlers_keep_working()
        {
            var command = new ObsoleteCommandHandler(_log, _correlation, _querySender.Object, _publisher.Object);
            var query = new ObsoleteQueryHandler(_log, _correlation, _querySender.Object);
            var @event = new ObsoleteEventHandler(_log, _correlation, _commandSender.Object, _querySender.Object);

            await command.HandleAsync(new SampleCommand("d"), _token);
            await @event.HandleAsync(new SampleEvent("e"), _token);

            Assert.Equal("result", command.QueryResult);
            Assert.Equal("result", await query.HandleAsync(new SampleQuery(1), _token));
            Assert.Equal("result", @event.QueryResult);
            Assert.Same(_log, command.Logger);
            Assert.Same(_log, query.Logger);
            Assert.Same(_log, @event.Logger);
            Assert.Same(_correlation, command.Corr);
            Assert.Same(_correlation, query.Corr);
            Assert.Same(_correlation, @event.Corr);
            _publisher.Verify(p => p.PublishAsync(It.IsAny<SampleEvent>(), _token), Times.Once);
            _publisher.Verify(p => p.PublishAsync(It.IsAny<IEnumerable<IEvent>>(), _token), Times.Once);
            _commandSender.Verify(s => s.SendAsync(It.IsAny<SampleCommand>(), _token), Times.Once);
        }

        [Fact]
        public async Task CommandForwarder_sends_transformed_command()
        {
            var forwarder = new Forwarder(_commandSender.Object);

            await forwarder.HandleAsync(new SampleCommand("x"), _token);

            _commandSender.Verify(s => s.SendAsync(forwarder.Output, _token), Times.Once);
        }

        [Fact]
        public void DurationMetric_observes_elapsed_milliseconds_on_dispose()
        {
            var service = new Mock<IMetricsService>();
            var metric = new object();

            using (service.Object.Observe(metric)) {
                Thread.Sleep(5);
            }

            service.Verify(s => s.Observe(metric, It.Is<double>(d => d >= 0)), Times.Once);
        }

        [Fact]
        public void Metric_attributes_hold_values()
        {
            var linear = new LinearHistogramMetric("lin", "Linear", 1, 2, 3);
            var exponential = new ExponentialHistogramMetric("exp", "Exp", 1, 2, 3);
            var field = new MetricFieldAttribute("field");

            Assert.Equal(("lin", "Linear", 1d, 2d, 3), (linear.Name, linear.Title, linear.Start, linear.Width, linear.Count));
            Assert.Equal(("exp", "Exp", 1d, 2d, 3), (exponential.Name, exponential.Title, exponential.Start, exponential.Factor, exponential.Count));
            Assert.Equal(("c", "C"), (new CounterMetric("c", "C").Name, new CounterMetric("c", "C").Title));
            Assert.Equal("g", new GaugeMetric("g", "G").Name);
            Assert.Equal("s", new SummaryMetric("s", "S").Name);
            Assert.Equal(("field", null), (field.Name, field.Title));
            Assert.Equal("T", new MetricFieldAttribute("f", "T").Title);
        }
    }
}
