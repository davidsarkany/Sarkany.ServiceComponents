using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autofac;
using Autofac.Core;
using Moq;
using RabbitMQ.Client;
using Serilog;
using ServiceComponents.Infrastructure.Rabbit;
using Xunit;

namespace ServiceComponents.UnitTests.Rabbit
{
    public class RabbitTopologyTests
    {
        private static string Describe(RecordingChannel.Declared d)
        {
            var args = d.Arguments == null || d.Arguments.Count == 0
                ? ""
                : " " + string.Join(",", d.Arguments.OrderBy(a => a.Key).Select(a => $"{a.Key}={a.Value}"));
            return d.Kind switch {
                "bind" => $"bind {d.Name} <- {d.Extra}",
                "exchange" => $"exchange {d.Name} {d.Extra} durable={d.Durable} autoDelete={d.AutoDelete}",
                _ => $"queue {d.Name} durable={d.Durable} autoDelete={d.AutoDelete} {d.Extra}{args}"
            };
        }

        [Fact]
        public void Retry_topology_is_declared_exactly()
        {
            var channel = new RecordingChannel();

            var result = channel.Object.AddRabbitRetry(null, "work", new[] { 1000, 5000 });

            Assert.Same(channel.Object, result);
            Assert.Equal(new[] {
                "queue work durable=False autoDelete=True exclusive=False x-dead-letter-exchange=work-dlx-wait-1000",
                "exchange work-dlx-wait-1000 direct durable=False autoDelete=True",
                "exchange work-dlx-retry-1000 direct durable=False autoDelete=True",
                "queue work-wait-1000 durable=False autoDelete=True exclusive=False x-dead-letter-exchange=work-dlx-retry-1000,x-message-ttl=1000",
                "queue work-retry-1000 durable=False autoDelete=True exclusive=False x-dead-letter-exchange=work-dlx-wait-5000",
                "bind work-wait-1000 <- work-dlx-wait-1000|",
                "bind work-retry-1000 <- work-dlx-retry-1000|",
                "exchange work-dlx-wait-5000 direct durable=False autoDelete=True",
                "exchange work-dlx-retry-5000 direct durable=False autoDelete=True",
                "queue work-wait-5000 durable=False autoDelete=True exclusive=False x-dead-letter-exchange=work-dlx-retry-5000,x-message-ttl=5000",
                "queue work-retry-5000 durable=False autoDelete=True exclusive=False x-dead-letter-exchange=work-dlx",
                "bind work-wait-5000 <- work-dlx-wait-5000|",
                "bind work-retry-5000 <- work-dlx-retry-5000|",
                "exchange work-dlx direct durable=False autoDelete=True",
                "queue work-dlx durable=False autoDelete=True exclusive=False",
                "bind work-dlx <- work-dlx|"
            }, channel.Topology.Select(Describe));
        }

        [Fact]
        public void Ttl_argument_is_an_int()
        {
            var channel = new RecordingChannel();

            channel.Object.AddRabbitRetry(null, "work", new[] { 1000 });

            Assert.IsType<int>(channel.Topology.Single(d => d.Kind == "queue" && d.Name == "work-wait-1000").Arguments["x-message-ttl"]);
        }

        [Fact]
        public void Retry_consumers_share_one_keyed_channel()
        {
            var connection = new Mock<IConnection>();
            var channel = new RecordingChannel();
            connection.Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(channel.Object);
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance(connection.Object).Keyed<IConnection>("consumer");
            builder.AddRabbitRetryConsumers("consumer", "work", new[] { 1000, 5000 }, "app");
            using var container = builder.Build();

            var first = container.ResolveKeyed<RabbitConsumer>("consumer-retry-1000");
            var second = container.ResolveKeyed<RabbitConsumer>("consumer-retry-5000");

            Assert.Equal("app-consumer-retry-1000", first.ConsumerTag);
            Assert.Equal("app-consumer-retry-5000", second.ConsumerTag);
            Assert.Same(channel.Object, container.ResolveKeyed<IChannel>("consumer-retry"));
            Assert.Equal(2, container.ResolveKeyed<IEnumerable<RabbitConsumer>>("__consumer__").Count());
            connection.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void Legacy_channel_registration_creates_channel_on_first_resolve()
        {
            var connection = new Mock<IConnection>();
            var channel = new RecordingChannel();
            connection.Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(channel.Object);
            var builder = new ContainerBuilder();
            builder.RegisterInstance(connection.Object).As<IConnection>();
            builder.AddRabbitChannel();
            builder.AddRabbitChannel(key: "other");
            using var container = builder.Build();

            connection.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Same(container.Resolve<IChannel>(), container.Resolve<IChannel>());
            container.ResolveKeyed<IChannel>("other");
            connection.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public void Legacy_connection_registration_validates_options_before_connecting()
        {
            var builder = new ContainerBuilder();
            builder.AddRabbitConnection(null, "client");
            builder.AddRabbitConnection(new Uri("amqp://localhost"), " ", "keyed");
            using var container = builder.Build();

            var error = Assert.Throws<DependencyResolutionException>(() => container.Resolve<IConnection>());
            Assert.Equal("EndpointUri is required for RabbitMQ connection configuration.", error.GetBaseException().Message);
            var keyed = Assert.Throws<DependencyResolutionException>(() => container.ResolveKeyed<IConnection>("keyed"));
            Assert.Equal("ClientName is required for RabbitMQ connection configuration.", keyed.GetBaseException().Message);
        }

        [Fact]
        public async System.Threading.Tasks.Task Async_registration_validates_arguments_eagerly()
        {
            var builder = new ContainerBuilder();

            Assert.Throws<ArgumentNullException>(() => builder.RegisterRabbitConnection((Uri)null, "client"));
            Assert.Throws<ArgumentException>(() => builder.RegisterRabbitConnection(new Uri("amqp://localhost"), " "));
            Assert.Throws<ArgumentNullException>(() => builder.RegisterRabbitConnection((Func<IComponentContext, IConnectionFactory>)null));
            await Assert.ThrowsAsync<ArgumentNullException>(() => ((ILifetimeScope)null).InitializeRabbitAsync());
        }

        [Fact]
        public void Connection_options_defaults_and_section_name()
        {
            var options = new RabbitConnectionOptions();

            Assert.Equal("ServiceComponents:RabbitConnectionOptions", RabbitConnectionOptions.ConfigurationSectionName);
            Assert.True(options.AutomaticRecoveryEnabled);
            Assert.Equal(120, options.HandshakeContinuationTimeoutInSeconds);
            Assert.Equal(60, options.RequestedHeartbeatInSeconds);
            Assert.Equal(2048, options.RequestedChannelMaxInSeconds);
            Assert.Equal(5, options.NetworkRecoveryIntervalInSeconds);
            Assert.False(options.Ssl);
            Assert.Equal("/", options.VirtualHost);
            Assert.Null(options.EndpointUri);
            Assert.Null(options.ClientName);
        }
    }
}
