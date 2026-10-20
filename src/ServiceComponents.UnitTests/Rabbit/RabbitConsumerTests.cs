using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Moq;
using RabbitMQ.Client;
using Serilog;
using ServiceComponents.Application;
using ServiceComponents.Application.Mediator;
using ServiceComponents.Infrastructure.CorrelationContext;
using ServiceComponents.Infrastructure.Mediator;
using ServiceComponents.Infrastructure.Rabbit;
using ServiceComponents.Infrastructure.Receivers;
using Xunit;

namespace ServiceComponents.UnitTests.Rabbit
{
    public class RabbitConsumerTests
    {
        public class Seen
        {
            public readonly List<(string what, string correlationId, string causationId, string currentId)> Items = new();
        }

        public class Handlers : IHandleCommand<SampleCommand>, IHandleQuery<SampleQuery, string>, IHandleEvent<SampleEvent>
        {
            private readonly Seen _seen;
            private readonly ICorrelation _correlation;

            public Handlers(Seen seen, ICorrelation correlation)
            {
                _seen = seen;
                _correlation = correlation;
            }

            private void Record(string what) => _seen.Items.Add((what, _correlation.CorrelationId, _correlation.CausationId, _correlation.CurrentId));

            public Task HandleAsync(SampleCommand command, CancellationToken cancellationToken = default)
            {
                Record("command:" + command.Data);
                return command.Data == "fail" ? throw new InvalidOperationException("boom") : Task.CompletedTask;
            }

            public Task<string> HandleAsync(SampleQuery query, CancellationToken cancellationToken = default)
            {
                Record("query:" + query.Number);
                return query.Number switch {
                    < 0 => throw new InvalidOperationException("boom"),
                    0 => Task.FromResult<string>(null),
                    _ => Task.FromResult("result-" + query.Number)
                };
            }

            public Task HandleAsync(SampleEvent @event, CancellationToken cancellationToken = default)
            {
                Record("event:" + @event.Data);
                return Task.CompletedTask;
            }
        }

        public class RawMessages : IReceiveRabbitMessage
        {
            public readonly List<string> Bodies = new();

            public Task ReceiveAsync(string body, RabbitMQ.Client.Events.BasicDeliverEventArgs args, CancellationToken cancellationToken)
            {
                Bodies.Add(body);
                return Task.CompletedTask;
            }
        }

        private readonly RecordingChannel _channel = new RecordingChannel();
        private readonly Seen _seen = new Seen();
        private ulong _tag;

        private async Task<(IContainer container, RabbitConsumer consumer)> Start(string consumerTag = null, Action<ContainerBuilder> configure = null)
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance(_channel.Object).As<IChannel>();
            builder.RegisterInstance(_seen);
            builder.AddMediator();
            builder.RegisterType<Handlers>().AsImplementedInterfaces();
            builder.AddCorrelationInfo();
            builder.AddReceivers();
            builder.AddRabbitReceivers();
            builder.AddRabbitReceiverCorrelationBehavior();
            builder.AddRabbitConsumer("work-queue", consumerTag);
            configure?.Invoke(builder);
            var container = builder.Build();
            var consumer = container.Resolve<RabbitConsumer>();
            await consumer.StartAsync();
            return (container, consumer);
        }

        private Task Deliver(string body, string type, BasicProperties properties = null)
        {
            properties ??= new BasicProperties();
            properties.Type = type;
            return _channel.Consumers["work-queue"].HandleBasicDeliverAsync("tag", ++_tag, false, "ex", "rk", properties, Encoding.UTF8.GetBytes(body));
        }

        private static string TypeName<T>() => $"{typeof(T).FullName}, {typeof(T).Assembly.GetName().Name}";

        [Fact]
        public async Task Command_is_dispatched_with_correlation_and_acked()
        {
            var (container, _) = await Start();
            await using var owner = container;

            await Deliver("{\"Data\":\"a\",\"CommandId\":\"c-1\"}", TypeName<SampleCommand>(), new BasicProperties {
                CorrelationId = "corr", Headers = new Dictionary<string, object> { ["causation_id"] = Encoding.UTF8.GetBytes("cause") }
            });

            Assert.Equal(new[] { ("command:a", "corr", "cause", "c-1") }, _seen.Items);
            Assert.Equal(new ulong[] { 1 }, _channel.Acks);
            Assert.Empty(_channel.Rejects);
            Assert.Empty(_channel.Publishes);
        }

        [Fact]
        public async Task Missing_correlation_id_is_generated()
        {
            var (container, _) = await Start();
            await using var owner = container;

            await Deliver("{\"Data\":\"a\",\"CommandId\":\"c-1\"}", TypeName<SampleCommand>());

            var item = Assert.Single(_seen.Items);
            Assert.True(Guid.TryParse(item.correlationId, out _));
            Assert.Null(item.causationId);
        }

        [Fact]
        public async Task Handler_failure_rejects_without_requeue()
        {
            var (container, _) = await Start();
            await using var owner = container;

            await Deliver("{\"Data\":\"fail\"}", TypeName<SampleCommand>());

            Assert.Equal(new[] { (1ul, false) }, _channel.Rejects);
            Assert.Empty(_channel.Acks);
        }

        [Fact]
        public async Task Query_is_acked_then_answered_on_reply_to()
        {
            var (container, _) = await Start();
            await using var owner = container;

            await Deliver("{\"Number\":5,\"QueryId\":\"q-1\"}", TypeName<SampleQuery>(), new BasicProperties { CorrelationId = "corr", ReplyTo = "reply-q" });

            Assert.Equal(new ulong[] { 1 }, _channel.Acks);
            var reply = Assert.Single(_channel.Publishes);
            Assert.Equal(("", "reply-q", false), (reply.Exchange, reply.RoutingKey, reply.Mandatory));
            Assert.Equal("\"result-5\"", reply.Body);
            Assert.Equal("corr", reply.Properties.CorrelationId);
            Assert.False(reply.Properties.IsTypePresent());
            Assert.False(reply.Properties.IsHeadersPresent());
        }

        [Fact]
        public async Task Query_failure_current_behavior_rejects_and_sends_no_reply()
        {
            var (container, _) = await Start();
            await using var owner = container;

            // BUG: the RPC caller is never answered and waits forever.
            await Deliver("{\"Number\":-1}", TypeName<SampleQuery>(), new BasicProperties { CorrelationId = "corr", ReplyTo = "reply-q" });

            Assert.Single(_channel.Rejects);
            Assert.Empty(_channel.Publishes);
        }

        [Fact]
        public async Task Null_query_result_is_answered_with_null()
        {
            var (container, _) = await Start();
            await using var owner = container;

            // Fixed in 0.3 (known issue #3): no reply used to be sent for a null result, so the caller waited forever.
            await Deliver("{\"Number\":0}", TypeName<SampleQuery>(), new BasicProperties { CorrelationId = "corr", ReplyTo = "reply-q" });

            Assert.Single(_channel.Acks);
            var reply = Assert.Single(_channel.Publishes);
            Assert.Equal(("reply-q", "null", "corr"), (reply.RoutingKey, reply.Body, reply.Properties.CorrelationId));
        }

        [Fact]
        public async Task Query_failure_reply_is_opt_in()
        {
            var (container, _) = await Start(configure: b => b.RegisterInstance(new RabbitMessagingOptions { ReplyOnQueryFailure = true }));
            await using var owner = container;

            await Deliver("{\"Number\":-1}", TypeName<SampleQuery>(), new BasicProperties { CorrelationId = "corr", ReplyTo = "reply-q" });

            Assert.Single(_channel.Rejects);
            var reply = Assert.Single(_channel.Publishes);
            Assert.Equal(("reply-q", "corr", "null"), (reply.RoutingKey, reply.Properties.CorrelationId, reply.Body));
            Assert.Equal("generic", reply.Properties.Headers["x-sc-error-kind"]);
            Assert.Equal("boom", reply.Properties.Headers["x-sc-error-message"]);
        }

        [Fact]
        public async Task Prefetch_count_is_applied_before_consuming()
        {
            var (container, _) = await Start(configure: b => b.RegisterInstance(new RabbitMessagingOptions { PrefetchCount = 25 }));
            await using var owner = container;

            _channel.Mock.Verify(c => c.BasicQosAsync(0, 25, false, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task No_prefetch_by_default()
        {
            var (container, _) = await Start();
            await using var owner = container;

            _channel.Mock.Verify(c => c.BasicQosAsync(It.IsAny<uint>(), It.IsAny<ushort>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Query_without_reply_to_is_acked_without_reply()
        {
            var (container, _) = await Start();
            await using var owner = container;

            await Deliver("{\"Number\":3}", TypeName<SampleQuery>(), new BasicProperties { CorrelationId = "corr" });

            Assert.Single(_channel.Acks);
            Assert.Empty(_channel.Publishes);
        }

        [Fact]
        public async Task Event_is_dispatched_and_acked()
        {
            var (container, _) = await Start();
            await using var owner = container;

            await Deliver("{\"Data\":\"e\",\"EventId\":\"e-1\"}", TypeName<SampleEvent>(), new BasicProperties { CorrelationId = "corr" });

            // Fixed in 0.3 (known issue #1): the correlation set by the Rabbit receiver reaches the event handlers.
            var item = Assert.Single(_seen.Items);
            Assert.Equal(("event:e", "corr", "e-1"), (item.what, item.correlationId, item.currentId));
            Assert.Single(_channel.Acks);
        }

        [Fact]
        public async Task Embedded_type_is_used_when_type_property_missing()
        {
            var (container, _) = await Start();
            await using var owner = container;

            await Deliver("{\"$type\":\"" + TypeName<SampleCommand>() + "\",\"Data\":\"t\"}", null);

            Assert.Equal("command:t", Assert.Single(_seen.Items).what);
        }

        [Fact]
        public async Task Unknown_message_without_raw_receiver_is_rejected()
        {
            var (container, _) = await Start();
            await using var owner = container;

            await Deliver("plain text", null);

            Assert.Single(_channel.Rejects);
        }

        [Fact]
        public async Task Unknown_message_goes_to_raw_receiver_when_registered()
        {
            var raw = new RawMessages();
            var (container, _) = await Start(configure: b => b.RegisterInstance(raw).As<IReceiveRabbitMessage>());
            await using var owner = container;

            await Deliver("plain text", null);

            Assert.Equal(new[] { "plain text" }, raw.Bodies);
            Assert.Single(_channel.Acks);
        }

        [Fact]
        public async Task Start_and_stop_use_consumer_tag()
        {
            var (container, consumer) = await Start("my-tag");
            await using var owner = container;

            Assert.Equal("my-tag", consumer.ConsumerTag);
            await consumer.StopAsync();
            _channel.Mock.Verify(c => c.BasicCancelAsync("my-tag", false, It.IsAny<CancellationToken>()));
        }

        [Fact]
        public async Task Generated_consumer_tag_is_kept()
        {
            var (container, consumer) = await Start();
            await using var owner = container;

            Assert.Equal("generated-tag", consumer.ConsumerTag);
            Assert.False(consumer.IsLive);
            Assert.Same(consumer, container.ResolveKeyed<RabbitConsumer>("__consumer__"));
            Assert.Same(consumer, container.Resolve<IRabbitConsumer>());
        }

        [Fact]
        public void Keyed_consumers_are_also_registered_under_the_shared_key()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance(_channel.Object).Keyed<IChannel>("consumer-1");
            builder.AddRabbitConsumer("q", "tag-1", "consumer-1", "consumer-1");
            using var container = builder.Build();

            Assert.Equal("tag-1", container.ResolveKeyed<RabbitConsumer>("consumer-1").ConsumerTag);
            Assert.Single(container.ResolveKeyed<IEnumerable<RabbitConsumer>>("__consumer__"));
            Assert.False(container.IsRegistered<RabbitConsumer>());
        }
    }
}
