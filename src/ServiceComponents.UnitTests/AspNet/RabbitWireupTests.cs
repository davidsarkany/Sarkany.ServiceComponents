using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using RabbitMQ.Client;
using Serilog;
using ServiceComponents.Application.Senders;
using ServiceComponents.AspNet.Wireup;
using ServiceComponents.Infrastructure.Rabbit;
using ServiceComponents.Infrastructure.Rabbit.Senders;
using ServiceComponents.UnitTests.Rabbit;
using Xunit;

namespace ServiceComponents.UnitTests.AspNet
{
    public class RabbitWireupTests
    {
        private readonly Dictionary<string, (Mock<IConnection> connection, List<RecordingChannel> channels)> _connections = new();

        private Mock<IConnection> Connection(string name)
        {
            var connection = new Mock<IConnection>();
            var channels = new List<RecordingChannel>();
            connection.Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => {
                    var channel = new RecordingChannel();
                    channels.Add(channel);
                    return channel.Object;
                });
            _connections[name] = (connection, channels);
            return connection;
        }

        /// <summary>
        /// Runs the container callbacks registered by <see cref="ServiceComponentsHostBuilder"/> against a plain
        /// ContainerBuilder, replacing the real RabbitMQ connections with mocks so no broker is needed.
        /// </summary>
        private IContainer Build(ServiceComponentsHostBuilder hostBuilder)
        {
            var configuration = new ConfigurationBuilder().Build();
            var context = new HostBuilderContext(new Dictionary<object, object>()) { Configuration = configuration };
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance<IConfiguration>(configuration);
            hostBuilder.ContainerBuilderCallbacks.ForEach(callback => callback(context, builder));
            builder.RegisterInstance(Connection("generic").Object).As<IConnection>();
            builder.RegisterInstance(Connection("publisher").Object).Keyed<IConnection>("publisher");
            builder.RegisterInstance(Connection("consumer").Object).Keyed<IConnection>("consumer");
            return builder.Build();
        }

        private static string Describe(RecordingChannel.Declared d) =>
            d.Kind == "bind" ? $"bind {d.Name} <- {d.Extra}" : $"{d.Kind} {d.Name}";

        [Fact]
        public void AddRabbit_declares_topology_and_starts_consumers_on_build()
        {
            using var container = Build(new ServiceComponentsHostBuilder().AddRabbit("amqp://localhost", "app", "app-queue", "app-exchange"));

            var generic = Assert.Single(_connections["generic"].channels);
            Assert.Equal(new[] { "exchange app-exchange", "queue app-queue", "bind app-queue <- app-exchange|" }, generic.Topology.Select(Describe));
            var exchange = generic.Topology.First();
            Assert.Equal(("direct", false, true), (exchange.Extra, exchange.Durable, exchange.AutoDelete));
            Assert.Null(generic.Topology.ElementAt(1).Arguments);

            var consumerChannels = _connections["consumer"].channels;
            Assert.Equal(Math.Max(1, Environment.ProcessorCount), consumerChannels.Count);
            Assert.All(consumerChannels, channel => Assert.True(channel.Consumers.ContainsKey("app-queue")));
            var tags = container.ResolveKeyed<IEnumerable<RabbitConsumer>>("__consumer__").Select(c => c.ConsumerTag).OrderBy(t => t).ToArray();
            Assert.Equal(Enumerable.Range(0, consumerChannels.Count).Select(i => $"app-consumer-{i}").OrderBy(t => t), tags);
        }

        [Fact]
        public void AddRabbit_registers_rabbit_keyed_senders_on_the_publisher_channel()
        {
            using var container = Build(new ServiceComponentsHostBuilder().AddRabbit("amqp://localhost", "app", "app-queue", "app-exchange", "rk"));

            Assert.IsType<RabbitEventPublisherProxy>(container.ResolveKeyed<IPublishEvent>("rabbit"));
            Assert.IsType<RabbitCommandSenderProxy>(container.ResolveKeyed<ISendCommand>("rabbit"));
            Assert.IsType<RabbitQuerySenderProxy>(container.ResolveKeyed<ISendQuery>("rabbit"));
            Assert.Single(_connections["publisher"].channels);
            Assert.Equal("bind app-queue <- app-exchange|rk", Describe(_connections["generic"].channels.Single().Topology.Last()));
        }

        [Fact]
        public void AddRabbit_with_retry_declares_retry_chain_and_retry_consumers()
        {
            using var container = Build(new ServiceComponentsHostBuilder().AddRabbit("amqp://localhost", "app", "q", "x", "", new[] { 100, 200 }));

            var topology = _connections["generic"].channels.Single().Topology.Select(Describe).ToArray();
            Assert.Equal("exchange x", topology.First());
            Assert.Contains("queue q-wait-100", topology);
            Assert.Contains("queue q-retry-200", topology);
            Assert.Equal("bind q <- x|", topology.Last());
            Assert.Equal(new[] { "app-consumer-retry-100", "app-consumer-retry-200" },
                new[] { container.ResolveKeyed<RabbitConsumer>("consumer-retry-100"), container.ResolveKeyed<RabbitConsumer>("consumer-retry-200") }.Select(c => c.ConsumerTag));
            Assert.Equal(Math.Max(1, Environment.ProcessorCount - 2) + 2, container.ResolveKeyed<IEnumerable<RabbitConsumer>>("__consumer__").Count());
        }

        [Fact]
        public void AddRabbit_configuration_overload_without_optional_builders()
        {
            // Fixed in 0.3 (known issue #6): the optional builders used to be invoked when omitted (NullReferenceException).
            using var container = Build(new ServiceComponentsHostBuilder().AddRabbit(c => "amqp://localhost", c => "app", c => "q", c => "x"));

            Assert.Equal("bind q <- x|", Describe(_connections["generic"].channels.Single().Topology.Last()));
        }

        [Fact]
        public async System.Threading.Tasks.Task AddRabbit_commands_and_retry_bindings_use_the_routing_key()
        {
            using var container = Build(new ServiceComponentsHostBuilder().AddRabbit("amqp://localhost", "app", "q", "x", "rk", new[] { 100 }));

            await container.ResolveKeyed<ISendCommand>("rabbit").SendAsync(new SampleCommand("c"));

            // Fixed in 0.3 (known issue #8): commands used to be sent with "" although the queue is bound with "rk", and
            // dead-lettered messages keep their routing key, so every retry step is bound with "rk" too.
            Assert.Equal("rk", Assert.Single(_connections["publisher"].channels.Single().Publishes).RoutingKey);
            var bindings = _connections["generic"].channels.Single().Topology.Where(d => d.Kind == "bind").Select(Describe).ToArray();
            Assert.Contains("bind q-wait-100 <- q-dlx-wait-100|rk", bindings);
            Assert.Contains("bind q-retry-100 <- q-dlx-retry-100|rk", bindings);
            Assert.Contains("bind q-dlx <- q-dlx|rk", bindings);
            Assert.Contains("bind q-wait-100 <- q-dlx-wait-100|", bindings);
        }
    }
}
