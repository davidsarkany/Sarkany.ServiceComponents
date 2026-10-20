using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Newtonsoft.Json;
using RabbitMQ.Client;
using Serilog;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Mediator;
using ServiceComponents.Application.Senders;
using ServiceComponents.AspNet.Exceptions;
using ServiceComponents.AspNet.HealthCheck;
using ServiceComponents.Core;
using ServiceComponents.Core.Exceptions;
using ServiceComponents.Core.Extensions;
using ServiceComponents.Core.Services;
using ServiceComponents.Domain;
using ServiceComponents.Infrastructure.Behaviors.CommandConstraints;
using ServiceComponents.Infrastructure.CorrelationContext;
using ServiceComponents.Infrastructure.EventRecorder;
using ServiceComponents.Infrastructure.Mediator;
using ServiceComponents.Infrastructure.Monitoring;
using ServiceComponents.Infrastructure.Rabbit;
using ServiceComponents.Infrastructure.Receivers;
using ServiceComponents.Infrastructure.Receivers.Loopback;
using ServiceComponents.Infrastructure.Senders.Loopback;
using ServiceComponents.UnitTests.Fixtures;
using Xunit;

namespace ServiceComponents.UnitTests
{
    /// <summary>
    /// Characterization of known defects that are not pinned elsewhere (from a code review of 0.2).
    /// Every test asserts the current, defective behavior; a fix must flip the test deliberately.
    /// </summary>
    public class KnownDefectsTests
    {
        // --- RabbitMQ connection options -------------------------------------------------------------------------

        private static ConnectionFactory CreateConnectionFactory(RabbitConnectionOptions options) =>
            (ConnectionFactory)typeof(RabbitAutofacExtensions)
                .GetMethod("CreateConnectionFactory", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { options });

        [Fact]
        public void Rabbit_connection_options_still_override_when_set()
        {
            var factory = CreateConnectionFactory(new RabbitConnectionOptions {
                EndpointUri = new Uri("amqp://broker.example.com/"), ClientName = "client", VirtualHost = "configured", Ssl = true
            });

            Assert.Equal("configured", factory.VirtualHost);
            Assert.True(factory.Ssl.Enabled);
            Assert.Equal("broker.example.com", factory.Ssl.ServerName);
        }

        [Fact]
        public void Rabbit_connection_factory_keeps_vhost_and_tls_from_uri()
        {
            var factory = CreateConnectionFactory(new RabbitConnectionOptions {
                EndpointUri = new Uri("amqps://user:pass@broker.example.com/prod-vhost"),
                ClientName = "client"
            });

            // Fixed in 0.3 (known issue #2): the default VirtualHost and Ssl options used to replace the values from the URI.
            Assert.Equal("prod-vhost", factory.VirtualHost);
            Assert.True(factory.Ssl.Enabled);
            Assert.Equal("broker.example.com", factory.Ssl.ServerName);
            Assert.Equal(5671, factory.Port);
            Assert.Equal("broker.example.com", factory.HostName);
            Assert.Equal("client", factory.ClientProvidedName);
        }

        // --- RabbitMQ consumer -----------------------------------------------------------------------------------

        [Fact]
        public async Task Rabbit_consumer_does_not_reject_after_ack_when_reply_fails()
        {
            var channel = new Rabbit.RecordingChannel {
                OnPublish = _ => throw new InvalidOperationException("reply failed")
            };
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance(channel.Object).As<IChannel>();
            builder.AddMediator();
            builder.RegisterInstance(new QueryHandler()).As<IHandleQuery<SampleQuery, string>>();
            builder.AddCorrelationInfo();
            builder.AddReceivers();
            builder.AddRabbitReceivers();
            builder.AddRabbitConsumer("q");
            await using var container = builder.Build();
            await container.Resolve<RabbitConsumer>().StartAsync();

            await channel.Consumers["q"].HandleBasicDeliverAsync("t", 7, false, "", "q",
                new BasicProperties { Type = $"{typeof(SampleQuery).FullName}, {typeof(SampleQuery).Assembly.GetName().Name}", ReplyTo = "reply", CorrelationId = "c" },
                System.Text.Encoding.UTF8.GetBytes("{\"Number\":1}"));

            // Fixed in 0.3 (known issue #7): the delivery used to be acknowledged and then rejected (406, channel closed).
            Assert.Equal(new ulong[] { 7 }, channel.Acks);
            Assert.Empty(channel.Rejects);
        }

        public class QueryHandler : IHandleQuery<SampleQuery, string>
        {
            public Task<string> HandleAsync(SampleQuery query, CancellationToken cancellationToken = default) => Task.FromResult("r");
        }

        // --- Api -------------------------------------------------------------------------------------------------

        [Fact]
        public void Command_without_id_constructor_parameter_current_behavior_gets_new_id_after_deserialization()
        {
            var original = new ReferenceApplication.Api.TestCommand("data");

            var copy = JsonConvert.DeserializeObject<ReferenceApplication.Api.TestCommand>(JsonConvert.SerializeObject(original));

            // Messages must accept an optional commandId/queryId/eventId constructor parameter to keep their id on the wire.
            Assert.NotEqual(original.CommandId, copy.CommandId);
        }

        // --- AspNet ----------------------------------------------------------------------------------------------

        [Fact]
        public async Task Exception_mapper_maps_empty_401_body()
        {
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("") };

            // Fixed in 0.3 (#13): e.g. a JWT bearer challenge (empty 401) maps to UnauthorizedAccessException.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new JsonExceptionMapper().ThrowExceptionIfNeeded(response));
        }

        [Fact]
        public async Task Error_middleware_logs_server_errors()
        {
            var log = new TestLog();
            var middleware = new ErrorHandlingMiddleware(_ => throw new ApplicationException("hidden"), log.Logger, new JsonExceptionMapper());
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Response.Body = new System.IO.MemoryStream();

            await middleware.Invoke(context);

            Assert.Equal(500, context.Response.StatusCode);
            // Fixed in 0.3: failures mapped to a server error are logged.
            var logged = Assert.Single(log.Events);
            Assert.Equal(Serilog.Events.LogEventLevel.Error, logged.Level);
            Assert.Equal("hidden", logged.Exception.Message);
        }

        [Fact]
        public async Task Error_middleware_logs_client_errors_at_debug_level()
        {
            var log = new TestLog();
            var middleware = new ErrorHandlingMiddleware(_ => throw new InvalidOperationException("bad"), log.Logger, new JsonExceptionMapper());
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Response.Body = new System.IO.MemoryStream();

            await middleware.Invoke(context);

            Assert.Equal(400, context.Response.StatusCode);
            Assert.Equal(Serilog.Events.LogEventLevel.Debug, Assert.Single(log.Events).Level);
        }

        [Fact]
        public async Task Error_middleware_rethrows_when_the_response_has_started()
        {
            var log = new TestLog();
            var middleware = new ErrorHandlingMiddleware(_ => throw new ApplicationException("late"), log.Logger, new JsonExceptionMapper());
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedResponseFeature());

            // Fixed in 0.3: writing an error response after the headers were sent threw an InvalidOperationException instead.
            Assert.Equal("late", (await Assert.ThrowsAsync<ApplicationException>(() => middleware.Invoke(context))).Message);
            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal(Serilog.Events.LogEventLevel.Error, Assert.Single(log.Events).Level);
        }

        private class StartedResponseFeature : Microsoft.AspNetCore.Http.Features.HttpResponseFeature
        {
            public override bool HasStarted => true;
        }

        [Fact]
        public async Task Consumer_health_check_tolerates_duplicate_and_missing_tags()
        {
            var consumers = new[] {
                Mock.Of<IRabbitConsumer>(c => c.ConsumerTag == "same" && c.IsLive),
                Mock.Of<IRabbitConsumer>(c => c.ConsumerTag == "same" && !c.IsLive),
                Mock.Of<IRabbitConsumer>(c => c.ConsumerTag == "same" && c.IsLive),
                Mock.Of<IRabbitConsumer>(c => c.ConsumerTag == null && c.IsLive)
            };

            // Fixed in 0.3: duplicate tags no longer throw, and an unhealthy entry is not overwritten.
            var result = await new RabbitMQConsumerChecker(consumers).CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext(), CancellationToken.None);

            Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
            Assert.Equal("Unhealthy", result.Data["same"]);
            Assert.Equal("Healthy", result.Data[""]);
        }

        // --- Loopback --------------------------------------------------------------------------------------------

        public class Recorder
        {
            public readonly List<string> Seen = new();
        }

        public class SampleEventHandler : IHandleEvent<SampleEvent>
        {
            private readonly Recorder _recorder;
            public SampleEventHandler(Recorder recorder) => _recorder = recorder;

            public Task HandleAsync(SampleEvent @event, CancellationToken cancellationToken = default)
            {
                _recorder.Seen.Add(@event.Data);
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task Routable_event_is_unwrapped_by_loopback()
        {
            var recorder = new Recorder();
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
            builder.RegisterInstance(recorder);
            builder.AddMediator();
            builder.RegisterType<SampleEventHandler>().As<IHandleEvent<SampleEvent>>();
            builder.AddCorrelationInfo();
            builder.AddReceivers();
            builder.AddLoopbackReceivers();
            builder.AddLoopbackReceiverCorrelationBehavior();
            builder.AddLoopbackEventPublisher();
            await using var container = builder.Build();

            await container.Resolve<IPublishEvent>().PublishAsync(new RoutableEvent(new SampleEvent("wrapped"), "key"));
            await container.Resolve<IPublishEvent>().PublishAsync(new SampleEvent("plain"));

            // Fixed in 0.3: only the RabbitMQ publisher used to unwrap RoutableEvent; in-process the inner event was lost.
            Assert.Equal(new[] { "wrapped", "plain" }, recorder.Seen);
        }
    }
}

/// <summary>Message type deliberately declared outside any namespace.</summary>
public class GlobalNamespaceCommand : ServiceComponents.Api.Mediator.Command
{
}
