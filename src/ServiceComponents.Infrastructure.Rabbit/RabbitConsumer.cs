using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;
using ServiceComponents.Infrastructure.Receivers;

namespace ServiceComponents.Infrastructure.Rabbit
{
    public interface IRabbitConsumer
    {
        public string ConsumerTag { get; }
        public bool IsLive { get; }
    }

    /// <summary>
    /// Consumes a queue with manual acknowledgement: each delivery is parsed and received in its own lifetime scope, then
    /// acknowledged; query results are sent to <c>ReplyTo</c>. A failing delivery is rejected without requeue
    /// (dead-lettered when a retry topology exists).
    /// </summary>
    public class RabbitConsumer : IRabbitConsumer
    {
        private readonly ILogger _log;
        private readonly ILifetimeScope _rootScope;
        private readonly IChannel _channel;
        private readonly string _queue;
        private readonly RabbitMessagingOptions _options;
        private readonly AsyncEventingBasicConsumer _consumer;
        private readonly RequestParser _parser = new();

        public string ConsumerTag { get; private set; }
        public bool IsLive => _consumer.IsRunning;

        public RabbitConsumer(ILogger log, ILifetimeScope rootScope, IChannel channel, string queue, string consumerTag = default)
            : this(log, rootScope, channel, queue, consumerTag, new RabbitMessagingOptions())
        {
        }

        internal RabbitConsumer(ILogger log, ILifetimeScope rootScope, IChannel channel, string queue, string consumerTag, RabbitMessagingOptions options)
        {
            _log = log;
            _rootScope = rootScope;
            _channel = channel;
            _queue = queue;
            _options = options ?? new RabbitMessagingOptions();
            ConsumerTag = consumerTag;
            _consumer = new AsyncEventingBasicConsumer(_channel);
            _consumer.ReceivedAsync += ConsumerOnReceivedAsync;
        }

        private async Task ConsumerOnReceivedAsync(object sender, BasicDeliverEventArgs e)
        {
            _log.Verbose("Receiving message from RabbitMQ queue '{queue}'", _queue);

            await using var scope = _rootScope.BeginLifetimeScope();

            var isQuery = false;
            object queryResult = null;
            try {
                await _parser.ParseAsync(
                    Encoding.UTF8.GetString(e.Body.Span),
                    e.BasicProperties.Type,
                    (command, _) => scope.Resolve<IReceiveRabbitCommand>().ReceiveAsync(command, e, CancellationToken.None),
                    async (query, _) => {
                        isQuery = true;
                        queryResult = await RabbitQueryInvoker.For(query).ReceiveAsync(scope.Resolve<IReceiveRabbitQuery>(), query, e, CancellationToken.None);
                    },
                    (@event, _) => scope.Resolve<IReceiveRabbitEvent>().ReceiveAsync(@event, e, CancellationToken.None),
                    (payload, _) => scope.Resolve<IReceiveRabbitMessage>().ReceiveAsync(payload, e, CancellationToken.None),
                    CancellationToken.None);
            }
            catch (Exception exception) {
                _log.Error(exception, "Exception during receiving message from RabbitMQ queue '{queue}'", _queue);
                await _channel.BasicRejectAsync(e.DeliveryTag, false);
                if (isQuery && _options.ReplyOnQueryFailure) {
                    await Reply(e, RabbitReplies.Error(e.BasicProperties.CorrelationId, exception), null);
                }

                return;
            }

            await _channel.BasicAckAsync(e.DeliveryTag, false);

            if (isQuery) {
                // A null result is answered too ("null"), so the caller gets default instead of waiting forever.
                await Reply(e, RabbitReplies.Result(e.BasicProperties.CorrelationId), queryResult);
            }
        }

        // Runs after the delivery has been acknowledged or rejected: a failure here must not settle it a second time,
        // which would make the broker close the channel.
        private async Task Reply(BasicDeliverEventArgs e, BasicProperties properties, object result)
        {
            if (string.IsNullOrEmpty(e.BasicProperties.ReplyTo)) {
                _log.Error("Received query on RabbitMQ but no reply_to address defined for sending query result");
                return;
            }

            try {
                await _channel.BasicPublishAsync("", e.BasicProperties.ReplyTo, false, properties, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result, Formatting.None)));
            }
            catch (Exception exception) {
                _log.Error(exception, "Sending query reply to '{replyTo}' failed", e.BasicProperties.ReplyTo);
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_options.PrefetchCount > 0) {
                await _channel.BasicQosAsync(0, _options.PrefetchCount, false, cancellationToken);
            }

            if (ConsumerTag == default) {
                ConsumerTag = await _channel.BasicConsumeAsync(_queue, false, _consumer, cancellationToken);
            }
            else {
                await _channel.BasicConsumeAsync(_queue, false, ConsumerTag, _consumer, cancellationToken);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            await _channel.BasicCancelAsync(ConsumerTag, cancellationToken: cancellationToken);
        }
    }
}
