using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using ServiceComponents.Application.Senders;
using ServiceComponents.AspNet.Wireup;
using ServiceComponents.Infrastructure.CorrelationContext;
using ServiceComponents.Infrastructure.Rabbit;
using ServiceComponents.UnitTests.Fixtures;
using Xunit;

namespace ServiceComponents.IntegrationTests
{
    [Collection("rabbitmq")]
    public class RabbitEndToEndTests
    {
        private readonly RabbitMqFixture _rabbit;

        public RabbitEndToEndTests(RabbitMqFixture rabbit) => _rabbit = rabbit;

        private HttpTestHost Host(string name, string queue, string exchange, int[] retry = null) =>
            new HttpTestHost(builder => builder
                .AddRabbit(_rabbit.ConnectionString, name, queue, exchange, "", retry)
                .AddCommandRouter(_ => "rabbit")
                .AddQueryRouter(_ => "rabbit")
                .AddEventRouter(_ => "rabbit"));

        private static async Task Eventually(Func<bool> condition, int timeoutMs = 15000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition()) {
                if (DateTime.UtcNow > deadline) {
                    throw new TimeoutException("Condition not met in time");
                }

                await Task.Delay(50);
            }
        }

        [Fact]
        public async Task Command_travels_through_the_broker_with_correlation()
        {
            await using var host = Host("cmd-app", "cmd-queue", "cmd-exchange");
            await using var scope = host.Host.Services.GetRequiredService<ILifetimeScope>().BeginLifetimeScope();
            var correlation = scope.Resolve<Correlation>();
            correlation.CorrelationId = "corr-1";
            correlation.CurrentId = "origin";

            await scope.Resolve<ISendCommand>().SendAsync(new HttpCommand("ok", "c-1"));

            await Eventually(() => host.Handled.Any());
            Assert.Equal(("command:ok", "corr-1", "origin", "c-1"), host.Handled.Single());
        }

        [Fact]
        public async Task Events_are_published_to_the_exchange()
        {
            await using var host = Host("evt-app", "evt-queue", "evt-exchange");

            await host.Host.Services.GetRequiredService<ILifetimeScope>().Resolve<IPublishEvent>().PublishAsync(new HttpEvent("e", "e-1"));

            // Handled once, although the fixture handler class implements three handler interfaces (fixed, #20).
            await Eventually(() => host.Handled.Length >= 1);
            await Task.Delay(500);
            Assert.Equal("event:e", Assert.Single(host.Handled).what);
        }

        [Fact]
        public async Task Query_rpc_works_when_queue_is_named_after_the_exchange()
        {
            await using var host = Host("rpc-app", "rpc-target", "rpc-target");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var result = await host.Host.Services.GetRequiredService<ILifetimeScope>().Resolve<ISendQuery>().SendAsync(new HttpQuery(21), cancellation.Token);

            Assert.Equal(42, result.Doubled);
            Assert.Equal("v21", result.Text);
        }

        [Fact]
        public async Task Query_rpc_current_behavior_is_lost_when_no_queue_matches_the_exchange_name()
        {
            await using var host = Host("lost-app", "lost-queue", "lost-exchange");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            // BUG: queries are published to the default exchange with the exchange name as routing key; with AddRabbit's
            // separate queue and exchange names nobody receives them and the caller waits until cancelled.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                host.Host.Services.GetRequiredService<ILifetimeScope>().Resolve<ISendQuery>().SendAsync(new HttpQuery(1), cancellation.Token));
            Assert.Empty(host.Handled);
        }

        [Fact]
        public async Task Commands_arrive_with_a_non_empty_routing_key()
        {
            await using var host = new HttpTestHost(builder => builder
                .AddRabbit(_rabbit.ConnectionString, "rk-app", "rk-queue", "rk-exchange", "rk")
                .AddCommandRouter(_ => "rabbit"));

            await host.Host.Services.GetRequiredService<ILifetimeScope>().Resolve<ISendCommand>().SendAsync(new HttpCommand("ok"));

            // Fixed (known issue #8): the command sender used an empty routing key and the commands got lost.
            await Eventually(() => host.Handled.Any());
            Assert.Equal("command:ok", host.Handled.Single().what);
        }

        [Fact]
        public async Task Routed_events_survive_the_retry_chain()
        {
            await using var host = new HttpTestHost(builder => builder
                .AddRabbit(_rabbit.ConnectionString, "rkr-app", "rkr-queue", "rkr-exchange", "rk", new[] { 200 }));

            // The event publisher uses the routing key "rk", so the first delivery arrives and the handler fails.
            await host.Host.Services.GetRequiredService<ILifetimeScope>().ResolveKeyed<IPublishEvent>("rabbit")
                .PublishAsync(new FailingEvent("e-1"));

            // Fixed (known issue #8): the retry chain is also bound with "rk", so the dead-lettered message (which keeps
            // its routing key) is retried and finally parked, instead of being dropped after the first attempt.
            await Eventually(() => host.Handled.Length == 2);
            await Eventually(() => _rabbit.MessageCount("rkr-queue-dlx").GetAwaiter().GetResult() == 1);
        }

        [Fact]
        public async Task Query_failure_is_reported_when_failure_replies_are_enabled()
        {
            await using var host = new HttpTestHost(builder => builder
                .AddRabbit(_rabbit.ConnectionString, "err-app", "err-target", "err-target")
                .AddQueryRouter(_ => "rabbit")
                .ConfigureContainer((_, container) => container.RegisterInstance(new RabbitMessagingOptions { ReplyOnQueryFailure = true, QueryTimeoutInSeconds = 10 })));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                host.Host.Services.GetRequiredService<ILifetimeScope>().Resolve<ISendQuery>().SendAsync(new FailingQuery()));

            Assert.Equal("query failed", error.Message);
        }

        [Fact]
        public async Task Query_timeout_option_ends_a_lost_query()
        {
            await using var host = new HttpTestHost(builder => builder
                .AddRabbit(_rabbit.ConnectionString, "to-app", "to-queue", "to-exchange")
                .AddQueryRouter(_ => "rabbit")
                .ConfigureContainer((_, container) => container.RegisterInstance(new RabbitMessagingOptions { QueryTimeoutInSeconds = 1 })));

            await Assert.ThrowsAsync<TimeoutException>(() =>
                host.Host.Services.GetRequiredService<ILifetimeScope>().Resolve<ISendQuery>().SendAsync(new HttpQuery(1)));
        }

        [Fact]
        public async Task Failed_message_without_retry_is_dropped()
        {
            await using var host = Host("drop-app", "drop-queue", "drop-exchange");

            await host.Host.Services.GetRequiredService<ILifetimeScope>().Resolve<ISendCommand>().SendAsync(new HttpCommand("crash"));

            await Eventually(() => host.Handled.Any());
            await Task.Delay(500);
            Assert.Single(host.Handled);
            Assert.Equal(0u, await _rabbit.MessageCount("drop-queue"));
        }

        [Fact]
        public async Task Failed_message_is_retried_then_dead_lettered()
        {
            await using var host = Host("retry-app", "retry-queue", "retry-exchange", new[] { 300, 600 });

            await host.Host.Services.GetRequiredService<ILifetimeScope>().Resolve<ISendCommand>().SendAsync(new HttpCommand("crash"));

            // Initial attempt plus one attempt per retry interval, then the message rests in the final DLX queue.
            await Eventually(() => host.Handled.Length == 3);
            await Eventually(() => _rabbit.MessageCount("retry-queue-dlx").GetAwaiter().GetResult() == 1);
            foreach (var queue in new[] { "retry-queue-wait-300", "retry-queue-retry-300", "retry-queue-wait-600", "retry-queue-retry-600" }) {
                Assert.True(await _rabbit.Exists(queue), queue);
            }
        }
    }
}
