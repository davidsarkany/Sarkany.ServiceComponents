using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Core.Extensions;

namespace ServiceComponents.Infrastructure.Rabbit.Senders
{
    /// <summary>
    /// Sends a query over RabbitMQ and waits for the reply (RPC): the query goes through the default exchange to the queue
    /// named like the configured exchange, the reply comes to a server-named, exclusive queue declared per query.
    /// </summary>
    public class RabbitQuerySender : ISendRabbitQuery
    {
        private readonly ILogger _log;
        private readonly IChannel _channel;
        private readonly string _exchange;
        private readonly string _routingKey;
        private readonly RabbitMessagingOptions _options;

        public RabbitQuerySender(ILogger log, IChannel channel, string exchange, string routingKey)
            : this(log, channel, exchange, routingKey, new RabbitMessagingOptions())
        {
        }

        internal RabbitQuerySender(ILogger log, IChannel channel, string exchange, string routingKey, RabbitMessagingOptions options)
        {
            _log = log;
            _channel = channel;
            _exchange = exchange;
            _routingKey = routingKey;
            _options = options ?? new RabbitMessagingOptions();
        }

        public async Task<TResult> SendAsync<TResult>(IQuery<TResult> query, BasicProperties basicProperties, CancellationToken cancellationToken = default)
        {
            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(query, Formatting.None));
            basicProperties.Type = query.AssemblyVersionlessQualifiedName();

            if (string.IsNullOrEmpty(basicProperties.CorrelationId)) {
                basicProperties.CorrelationId = Guid.NewGuid().ToString();
            }

            using var timeout = _options.QueryTimeoutInSeconds.HasValue ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
            timeout?.CancelAfter(TimeSpan.FromSeconds(_options.QueryTimeoutInSeconds.GetValueOrDefault()));
            var token = timeout?.Token ?? cancellationToken;

            // Awaited asynchronously; the former blocking wait starved the thread pool under concurrent queries.
            var reply = new TaskCompletionSource<(string body, IReadOnlyBasicProperties properties)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var replyConsumer = new AsyncEventingBasicConsumer(_channel);
            replyConsumer.ReceivedAsync += (_, args) => {
                if (args.BasicProperties.CorrelationId == basicProperties.CorrelationId) {
                    reply.TrySetResult((Encoding.UTF8.GetString(args.Body.Span), args.BasicProperties));
                }

                return Task.CompletedTask;
            };

            var replyQueueName = (await _channel.QueueDeclareAsync(cancellationToken: token)).QueueName;
            basicProperties.ReplyTo = replyQueueName;
            try {
                await _channel.BasicConsumeAsync(replyQueueName, autoAck: true, consumer: replyConsumer, cancellationToken: token);

                _log.ForContext("query", query, true).Verbose("Sending {queryType} using RabbitMQ sender to exchange '{exchange}', routing-key: '{routingKey}'", query.DisplayName(), _exchange, _routingKey);
                await _channel.BasicPublishAsync("", _exchange, false, basicProperties, body, token);

                (string body, IReadOnlyBasicProperties properties) response;
                await using (token.Register(() => reply.TrySetCanceled(token))) {
                    response = await reply.Task;
                }

                RabbitReplies.ThrowIfError(response.properties);
                return JsonConvert.DeserializeObject<TResult>(response.body);
            }
            catch (OperationCanceledException) when (timeout?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested) {
                throw new TimeoutException(FormattableString.Invariant($"No reply to {query.DisplayName()} from '{_exchange}' within {_options.QueryTimeoutInSeconds} s"));
            }
            finally {
                // Also on failure and cancellation, so reply queues do not pile up.
                try {
                    await _channel.QueueDeleteAsync(replyQueueName, false, false, cancellationToken: CancellationToken.None);
                }
                catch (Exception exception) {
                    _log.Warning(exception, "Deleting reply queue {queue} failed", replyQueueName);
                }
            }
        }
    }
}
