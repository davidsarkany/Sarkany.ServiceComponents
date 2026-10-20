using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using RabbitMQ.Client;

namespace ServiceComponents.UnitTests.Rabbit
{
    /// <summary>
    /// Mocked <see cref="IChannel"/> recording every publish, ack, reject and topology declaration.
    /// </summary>
    public class RecordingChannel
    {
        public record Published(string Exchange, string RoutingKey, bool Mandatory, BasicProperties Properties, string Body);

        public record Declared(string Kind, string Name, IDictionary<string, object> Arguments, bool Durable, bool AutoDelete, string Extra);

        public Mock<IChannel> Mock { get; } = new Mock<IChannel>();
        public IChannel Object => Mock.Object;

        public ConcurrentQueue<Published> Publishes { get; } = new();
        public ConcurrentQueue<ulong> Acks { get; } = new();
        public ConcurrentQueue<(ulong tag, bool requeue)> Rejects { get; } = new();
        public ConcurrentQueue<Declared> Topology { get; } = new();
        public ConcurrentQueue<string> DeletedQueues { get; } = new();
        public ConcurrentDictionary<string, IAsyncBasicConsumer> Consumers { get; } = new();

        /// <summary>Invoked synchronously after each publish (e.g. to simulate an RPC reply).</summary>
        public Func<Published, Task> OnPublish { get; set; } = _ => Task.CompletedTask;

        public RecordingChannel()
        {
            Mock.Setup(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<BasicProperties>(),
                    It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns((string exchange, string routingKey, bool mandatory, BasicProperties properties, ReadOnlyMemory<byte> body, CancellationToken _) => {
                    var published = new Published(exchange, routingKey, mandatory, properties, Encoding.UTF8.GetString(body.Span));
                    Publishes.Enqueue(published);
                    return new ValueTask(OnPublish(published));
                });

            Mock.Setup(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((ulong tag, bool _, CancellationToken _) => {
                    Acks.Enqueue(tag);
                    return ValueTask.CompletedTask;
                });

            Mock.Setup(c => c.BasicRejectAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((ulong tag, bool requeue, CancellationToken _) => {
                    Rejects.Enqueue((tag, requeue));
                    return ValueTask.CompletedTask;
                });

            Mock.Setup(c => c.BasicConsumeAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object>>(), It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
                .Returns((string queue, bool _, string tag, bool _, bool _, IDictionary<string, object> _, IAsyncBasicConsumer consumer, CancellationToken _) => {
                    Consumers[queue] = consumer;
                    return Task.FromResult(string.IsNullOrEmpty(tag) ? "generated-tag" : tag);
                });

            Mock.Setup(c => c.QueueDeclareAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((string queue, bool durable, bool exclusive, bool autoDelete, IDictionary<string, object> arguments, bool _, bool _, CancellationToken _) => {
                    var name = string.IsNullOrEmpty(queue) ? "amq.gen-reply" : queue;
                    Topology.Enqueue(new Declared("queue", name, arguments, durable, autoDelete, $"exclusive={exclusive}"));
                    return Task.FromResult(new QueueDeclareOk(name, 0, 0));
                });

            Mock.Setup(c => c.ExchangeDeclareAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((string exchange, string type, bool durable, bool autoDelete, IDictionary<string, object> arguments, bool _, bool _, CancellationToken _) => {
                    Topology.Enqueue(new Declared("exchange", exchange, arguments, durable, autoDelete, type));
                    return Task.CompletedTask;
                });

            Mock.Setup(c => c.QueueBindAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IDictionary<string, object>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((string queue, string exchange, string routingKey, IDictionary<string, object> arguments, bool _, CancellationToken _) => {
                    Topology.Enqueue(new Declared("bind", queue, arguments, false, false, $"{exchange}|{routingKey}"));
                    return Task.CompletedTask;
                });

            Mock.Setup(c => c.QueueDeleteAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((string queue, bool _, bool _, bool _, CancellationToken _) => {
                    DeletedQueues.Enqueue(queue);
                    return Task.FromResult(0u);
                });
        }
    }
}
