using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Moq;
using RabbitMQ.Client;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Senders;
using ServiceComponents.Infrastructure.CorrelationContext;
using ServiceComponents.Infrastructure.Rabbit;
using Xunit;

namespace ServiceComponents.UnitTests.Rabbit
{
    public class RabbitSenderTests
    {
        private static IContainer Build(RecordingChannel channel, Action<ContainerBuilder> configure, bool correlation = true)
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance(channel.Object).As<IChannel>();
            builder.AddCorrelationInfo();
            if (correlation) {
                builder.AddRabbitSenderCorrelationBehavior();
            }

            configure(builder);
            return builder.Build();
        }

        private static string TypeName<T>() => $"{typeof(T).FullName}, {typeof(T).Assembly.GetName().Name}";

        [Fact]
        public async Task Command_wire_format()
        {
            var channel = new RecordingChannel();
            await using var container = Build(channel, b => b.AddRabbitCommandSender("orders", ""));
            await using var scope = container.BeginLifetimeScope();
            var correlation = scope.Resolve<Correlation>();
            correlation.CorrelationId = "corr-1";
            correlation.CurrentId = "current-1";

            await scope.Resolve<ISendCommand>().SendAsync(new SampleCommand("payload", "c-1"));

            var published = Assert.Single(channel.Publishes);
            Assert.Equal("orders", published.Exchange);
            Assert.Equal("", published.RoutingKey);
            Assert.False(published.Mandatory);
            Assert.Equal("{\"Data\":\"payload\",\"CommandId\":\"c-1\"}", published.Body);
            Assert.Equal(TypeName<SampleCommand>(), published.Properties.Type);
            Assert.Equal("corr-1", published.Properties.CorrelationId);
            Assert.Equal("current-1", published.Properties.Headers["causation_id"]);
            Assert.Single(published.Properties.Headers);
            Assert.False(published.Properties.IsContentTypePresent());
            Assert.False(published.Properties.IsDeliveryModePresent());
            Assert.False(published.Properties.IsMessageIdPresent());
            Assert.False(published.Properties.IsReplyToPresent());
            Assert.False(published.Properties.IsTimestampPresent());
        }

        [Fact]
        public async Task Missing_correlation_generates_id_and_omits_causation()
        {
            var channel = new RecordingChannel();
            await using var container = Build(channel, b => b.AddRabbitCommandSender("orders"));

            await container.Resolve<ISendCommand>().SendAsync(new SampleCommand("p"));

            var published = Assert.Single(channel.Publishes);
            Assert.True(Guid.TryParse(published.Properties.CorrelationId, out _));
            Assert.Empty(published.Properties.Headers);
            // Default routing key is passed through as null.
            Assert.Null(published.RoutingKey);
        }

        [Fact]
        public async Task Without_correlation_behavior_no_correlation_is_set()
        {
            var channel = new RecordingChannel();
            await using var container = Build(channel, b => b.AddRabbitCommandSender("orders", ""), correlation: false);

            await container.Resolve<ISendCommand>().SendAsync(new SampleCommand("p"));

            Assert.False(Assert.Single(channel.Publishes).Properties.IsCorrelationIdPresent());
        }

        [Fact]
        public async Task Event_wire_format()
        {
            var channel = new RecordingChannel();
            await using var container = Build(channel, b => b.AddRabbitEventPublisher("events", "default-key"));

            await container.Resolve<IPublishEvent>().PublishAsync(new SampleEvent("d", "e-1"));
            await container.Resolve<IPublishEvent>().PublishAsync(new IEvent[] { new SampleEvent("x", "e-2"), new SampleEvent("y", "e-3") });

            var published = channel.Publishes.ToArray();
            Assert.Equal(3, published.Length);
            Assert.All(published, p => Assert.Equal(("events", "default-key", false), (p.Exchange, p.RoutingKey, p.Mandatory)));
            Assert.Equal(new[] { "e-1", "e-2", "e-3" }, published.Select(p => p.Properties.MessageId));
            Assert.Equal(TypeName<SampleEvent>(), published[0].Properties.Type);
            Assert.Equal("{\"Data\":\"d\",\"EventId\":\"e-1\"}", published[0].Body);
        }

        [Fact]
        public async Task Routable_event_sends_inner_event_with_its_routing_key()
        {
            var channel = new RecordingChannel();
            await using var container = Build(channel, b => b.AddRabbitEventPublisher("events", "default-key"));

            await container.Resolve<IPublishEvent>().PublishAsync(new RoutableEvent(new SampleEvent("d", "e-9"), "special"));

            var published = Assert.Single(channel.Publishes);
            Assert.Equal("special", published.RoutingKey);
            Assert.Equal("e-9", published.Properties.MessageId);
            Assert.Equal(TypeName<SampleEvent>(), published.Properties.Type);
            Assert.Equal("{\"Data\":\"d\",\"EventId\":\"e-9\"}", published.Body);
        }

        [Fact]
        public async Task Keyed_senders_and_channels()
        {
            var defaultChannel = new RecordingChannel();
            var publisherChannel = new RecordingChannel();
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance(defaultChannel.Object).As<IChannel>();
            builder.RegisterInstance(publisherChannel.Object).Keyed<IChannel>("publisher");
            builder.AddCorrelationInfo();
            builder.AddRabbitEventPublisher("events", "rk", channelKey: "publisher", key: "rabbit");
            builder.AddRabbitCommandSender("commands", "", "publisher", "rabbit");
            await using var container = builder.Build();

            Assert.False(container.IsRegistered<IPublishEvent>());
            await container.ResolveKeyed<IPublishEvent>("rabbit").PublishAsync(new SampleEvent("d"));
            await container.ResolveKeyed<ISendCommand>("rabbit").SendAsync(new SampleCommand("d"));

            Assert.Empty(defaultChannel.Publishes);
            Assert.Equal(new[] { "events", "commands" }, publisherChannel.Publishes.Select(p => p.Exchange));
        }

        [Fact]
        public async Task Query_rpc_wire_format()
        {
            var channel = new RecordingChannel();
            channel.OnPublish = async published => {
                var consumer = channel.Consumers[published.Properties.ReplyTo];
                await consumer.HandleBasicDeliverAsync("reply-tag", 1, false, "", published.Properties.ReplyTo,
                    new BasicProperties { CorrelationId = "unrelated" }, Encoding.UTF8.GetBytes("\"wrong\""));
                await consumer.HandleBasicDeliverAsync("reply-tag", 2, false, "", published.Properties.ReplyTo,
                    new BasicProperties { CorrelationId = published.Properties.CorrelationId }, Encoding.UTF8.GetBytes("\"answer\""));
            };
            await using var container = Build(channel, b => b.AddRabbitQuerySender("queries-queue", "ignored"));
            await using var scope = container.BeginLifetimeScope();
            scope.Resolve<Correlation>().CorrelationId = "corr-q";

            var result = await scope.Resolve<ISendQuery>().SendAsync(new SampleQuery(7, "q-1"));

            Assert.Equal("answer", result);
            var published = Assert.Single(channel.Publishes);
            // Queries go through the default exchange, using the configured "exchange" as routing key (queue name).
            Assert.Equal("", published.Exchange);
            Assert.Equal("queries-queue", published.RoutingKey);
            Assert.Equal("amq.gen-reply", published.Properties.ReplyTo);
            Assert.Equal("corr-q", published.Properties.CorrelationId);
            Assert.Equal(TypeName<SampleQuery>(), published.Properties.Type);
            Assert.Equal("{\"Number\":7,\"QueryId\":\"q-1\"}", published.Body);

            var replyQueue = Assert.Single(channel.Topology);
            Assert.Equal(("queue", "exclusive=True", true), (replyQueue.Kind, replyQueue.Extra, replyQueue.AutoDelete));
            Assert.Equal(new[] { "amq.gen-reply" }, channel.DeletedQueues);
            channel.Mock.Verify(c => c.BasicConsumeAsync("amq.gen-reply", true, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<System.Collections.Generic.IDictionary<string, object>>(), It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()));
        }

        [Fact]
        public async Task Query_without_reply_waits_until_cancelled_and_cleans_up()
        {
            var channel = new RecordingChannel();
            await using var container = Build(channel, b => b.AddRabbitQuerySender("queries-queue"));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            // By default there is no timeout, only the caller's token ends the wait.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                container.Resolve<ISendQuery>().SendAsync(new SampleQuery(1), cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10)));
            // Fixed in 0.3 (known issue #3): the reply queue used to be left behind.
            Assert.Equal(new[] { "amq.gen-reply" }, channel.DeletedQueues);
        }

        [Fact]
        public async Task Query_timeout_option_throws_timeout_exception()
        {
            var channel = new RecordingChannel();
            await using var container = Build(channel, b => {
                b.RegisterInstance(new RabbitMessagingOptions { QueryTimeoutInSeconds = 0.1 });
                b.AddRabbitQuerySender("queries-queue");
            });

            var error = await Assert.ThrowsAsync<TimeoutException>(() => container.Resolve<ISendQuery>().SendAsync(new SampleQuery(1)).WaitAsync(TimeSpan.FromSeconds(10)));

            Assert.StartsWith("No reply to SampleQuery from 'queries-queue' within 0.1 s", error.Message);
            Assert.Single(channel.DeletedQueues);
        }

        [Theory]
        [InlineData("business", typeof(ServiceComponents.Core.Exceptions.BusinessException))]
        [InlineData("notfound", typeof(ServiceComponents.Core.Exceptions.NotFoundException))]
        [InlineData("security", typeof(UnauthorizedAccessException))]
        [InlineData("validation", typeof(FluentValidation.ValidationException))]
        [InlineData("generic", typeof(InvalidOperationException))]
        [InlineData("internal", typeof(Exception))]
        public async Task Error_replies_are_thrown(string kind, Type expected)
        {
            var channel = new RecordingChannel();
            channel.OnPublish = async published => {
                await channel.Consumers[published.Properties.ReplyTo].HandleBasicDeliverAsync("reply-tag", 1, false, "", published.Properties.ReplyTo,
                    new BasicProperties {
                        CorrelationId = published.Properties.CorrelationId,
                        Headers = new System.Collections.Generic.Dictionary<string, object> {
                            ["x-sc-error-kind"] = Encoding.UTF8.GetBytes(kind),
                            ["x-sc-error-message"] = Encoding.UTF8.GetBytes("failed remotely"),
                            ["x-sc-error-code"] = 7
                        }
                    }, Encoding.UTF8.GetBytes("null"));
            };
            await using var container = Build(channel, b => b.AddRabbitQuerySender("queries-queue"));

            var error = await Assert.ThrowsAnyAsync<Exception>(() => container.Resolve<ISendQuery>().SendAsync(new SampleQuery(1)));

            Assert.Equal(expected, error.GetType());
            Assert.Equal("failed remotely", error.Message);
        }

        [Fact]
        public async Task Event_publisher_uses_mandatory_and_basic_properties_template()
        {
            var channel = new RecordingChannel();
            var template = new BasicProperties { DeliveryMode = DeliveryModes.Persistent, ContentType = "application/json" };
            await using var container = Build(channel, b => b.AddRabbitEventPublisher("events", "rk", mandatory: true, basicProperties: template));

            await container.Resolve<IPublishEvent>().PublishAsync(new SampleEvent("d"));

            // Fixed in 0.3: both arguments used to be ignored.
            var published = Assert.Single(channel.Publishes);
            Assert.True(published.Mandatory);
            Assert.Equal(DeliveryModes.Persistent, published.Properties.DeliveryMode);
            Assert.Equal("application/json", published.Properties.ContentType);
            Assert.False(template.IsCorrelationIdPresent());
        }
    }
}
