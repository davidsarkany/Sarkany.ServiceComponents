using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentNHibernate.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using NHibernate;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Application.Senders;
using ServiceComponents.AspNet.Wireup;
using ServiceComponents.Core.Services;
using ServiceComponents.Infrastructure.CouchDB;
using ServiceComponents.Infrastructure.NHibernate;
using ServiceComponents.Infrastructure.Redis;
using StackExchange.Redis;
using Xunit;
using ISession = NHibernate.ISession;
using IServer = Microsoft.AspNetCore.Hosting.Server.IServer;

namespace ServiceComponents.UnitTests.Integrations
{
    public class IntegrationPackageTests
    {
        [Fact]
        public void Redis_connection_is_lazy_and_database_uses_requested_index()
        {
            var multiplexer = new Mock<IConnectionMultiplexer>();
            var database = Mock.Of<IDatabase>();
            multiplexer.Setup(m => m.GetDatabase(3, null)).Returns(database);
            var builder = new ContainerBuilder();
            builder.AddRedisConnection("localhost:1,abortConnect=false,connectTimeout=1");
            builder.AddRedisConnection("localhost:1", "keyed");
            builder.RegisterInstance(multiplexer.Object).Keyed<IConnectionMultiplexer>("mock");
            builder.AddRedisDatabase(3, "mock");
            builder.AddRedisDatabase(3, "mock", "db-key");

            // Building performs no I/O: connections are only opened when resolved.
            using var container = builder.Build();

            Assert.Same(database, container.Resolve<IDatabase>());
            Assert.Same(database, container.ResolveKeyed<IDatabase>("db-key"));
            Assert.True(container.IsRegistered<IConnectionMultiplexer>());
            Assert.True(container.IsRegisteredWithKey<IConnectionMultiplexer>("keyed"));
        }

        private static IServiceCollection ServicesOf(ServiceComponentsHostBuilder builder)
        {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder().Build();
            foreach (var callback in (List<Action<IConfiguration, IServiceCollection>>)typeof(ServiceComponentsHostBuilder)
                         .GetField("_serviceCollectionCallbacks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                         .GetValue(builder)!) {
                callback(configuration, services);
            }

            return services;
        }

        [Fact]
        public void Redis_distributed_cache_overloads_configure_options()
        {
            var plain = ServicesOf(new ServiceComponentsHostBuilder().AddRedisDistributedCache("redis:6379", "inst:")).BuildServiceProvider();
            var parsed = ServicesOf(new ServiceComponentsHostBuilder().AddRedisDistributedCache("redis:6380,password=x")).BuildServiceProvider();
            var built = ServicesOf(new ServiceComponentsHostBuilder().AddRedisDistributedCache(o => o.EndPoints.Add("redis", 6381))).BuildServiceProvider();

            var plainOptions = plain.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
            Assert.Equal(("redis:6379", "inst:"), (plainOptions.Configuration, plainOptions.InstanceName));
            Assert.Equal("x", parsed.GetRequiredService<IOptions<RedisCacheOptions>>().Value.ConfigurationOptions.Password);
            Assert.Equal("Unspecified/redis:6381", built.GetRequiredService<IOptions<RedisCacheOptions>>().Value.ConfigurationOptions.EndPoints.Single().ToString());
            Assert.IsAssignableFrom<RedisCache>(plain.GetRequiredService<IDistributedCache>());
        }

        [Fact]
        public void Redis_host_extensions_register_connection_and_database()
        {
            foreach (var hostBuilder in new[] {
                         new ServiceComponentsHostBuilder().AddRedis("localhost:1"),
                         new ServiceComponentsHostBuilder().AddRedis(configuration => "localhost:1")
                     }) {
                var builder = new ContainerBuilder();
                var context = new HostBuilderContext(new Dictionary<object, object>()) { Configuration = new ConfigurationBuilder().Build() };
                hostBuilder.ContainerBuilderCallbacks.ForEach(callback => callback(context, builder));
                using var container = builder.Build();

                Assert.True(container.IsRegistered<IConnectionMultiplexer>());
                Assert.True(container.IsRegistered<IDatabase>());
            }
        }

        public class Thing
        {
            public virtual long Id { get; set; }
            public virtual string Name { get; set; }
        }

        public class ThingMap : ClassMap<Thing>
        {
            public ThingMap()
            {
                Id(x => x.Id);
                Map(x => x.Name);
            }
        }

        private const string UnreachablePostgres = "Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p;Timeout=1";

        [Fact]
        public void NHibernate_module_builds_the_session_factory_on_container_build()
        {
            NHibernate.Cfg.Configuration exposed = null;
            var builder = new ContainerBuilder();
            builder.RegisterModule(new NhibernateModule(UnreachablePostgres,
                map => map.FluentMappings.Add<ThingMap>(),
                configuration => exposed = configuration));

            // The session factory is auto-activated (the session no longer is), and building it reads keywords from the
            // database: a reachable PostgreSQL is still required at startup.
            var error = Assert.Throws<Autofac.Core.DependencyResolutionException>(() => builder.Build());

            Assert.Contains("Npgsql", error.GetBaseException().GetType().FullName + error.GetBaseException().StackTrace);
            Assert.NotNull(exposed);
            Assert.StartsWith("NHibernate.Dialect.PostgreSQL82Dialect,", exposed.GetProperty("dialect"));
            Assert.Contains(exposed.ClassMappings, mapping => mapping.MappedClass == typeof(Thing));
        }

        [Fact]
        public void NHibernate_host_extensions_register_module()
        {
            foreach (var hostBuilder in new[] {
                         new ServiceComponentsHostBuilder().AddNHibernate(UnreachablePostgres, map => map.FluentMappings.Add<ThingMap>(), _ => { }),
                         new ServiceComponentsHostBuilder().AddNHibernate(c => UnreachablePostgres, map => map.FluentMappings.Add<ThingMap>(), _ => { })
                     }) {
                var builder = new ContainerBuilder();
                var context = new HostBuilderContext(new Dictionary<object, object>()) { Configuration = new ConfigurationBuilder().Build() };
                hostBuilder.ContainerBuilderCallbacks.ForEach(callback => callback(context, builder));

                Assert.Throws<Autofac.Core.DependencyResolutionException>(() => builder.Build());
            }
        }

        [Fact]
        public async Task NHibernate_event_publisher_stores_events_and_publishes_on_dispose()
        {
            var session = new Mock<ISession>();
            var saved = new List<UnpublishedEventEntity>();
            session.Setup(s => s.SaveAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Callback((object entity, CancellationToken _) => saved.Add((UnpublishedEventEntity)entity))
                .ReturnsAsync(1L);
            var clock = Mock.Of<IClock>(c => c.UtcNow == new DateTime(2024, 1, 1));
            var next = new Mock<IPublishEvent>();
            var behavior = new NHibernateEventPublisherBehavior(session.Object, clock, next.Object);

            await behavior.PublishAsync(new SampleEvent("a", "e-1"));
            await behavior.PublishAsync(new IEvent[] { new SampleEvent("b", "e-2") });
            next.Verify(n => n.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Never);

            behavior.Dispose();

            Assert.Equal(2, saved.Count);
            Assert.Equal(new DateTime(2024, 1, 1), saved[0].TimeStamp);
            Assert.Equal(typeof(SampleEvent).ToString(), saved[0].EventType);
            Assert.StartsWith("{\"$type\":\"ServiceComponents.UnitTests.SampleEvent, ServiceComponents.UnitTests\"", saved[0].EventBody);
            next.Verify(n => n.PublishAsync(It.IsAny<IEvent>(), CancellationToken.None), Times.Exactly(2));

            // Fixed in 0.3: disposing again does not publish the events a second time.
            behavior.Dispose();
            next.Verify(n => n.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task NHibernate_event_publisher_publishes_on_async_dispose_of_the_scope()
        {
            var next = new Mock<IPublishEvent>();
            var builder = new ContainerBuilder();
            builder.RegisterInstance(Mock.Of<ISession>());
            builder.RegisterInstance(Mock.Of<IClock>());
            builder.RegisterInstance(next.Object);
            builder.RegisterType<NHibernateEventPublisherBehavior>().InstancePerLifetimeScope();
            await using var container = builder.Build();

            await using (var scope = container.BeginLifetimeScope()) {
                await scope.Resolve<NHibernateEventPublisherBehavior>().PublishAsync(new SampleEvent("a", "e-1"));
            }

            next.Verify(n => n.PublishAsync(It.Is<IEvent>(e => e.EventId == "e-1"), CancellationToken.None), Times.Once);
        }

        [Fact]
        public async Task CouchDb_configuration_source_rejects_error_responses()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var app = builder.Build();
            app.MapGet("/config-db/service", () => Results.Text("{\"error\":\"not_found\",\"reason\":\"missing\"}", "application/json", statusCode: 404));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

            var source = new CouchDbConfigurationSource(new ServiceConfigurationOptions { Uri = new Uri(address + "/"), Database = "config-db", Document = "service" });

            // Fixed in 0.3: the error document used to be loaded as configuration.
            var error = Assert.Throws<InvalidOperationException>(() => new ConfigurationBuilder().Add(source).Build());
            Assert.Contains("config-db/service", error.Message);
            Assert.Contains("404", error.Message);
        }

        [Fact]
        public async Task CouchDb_configuration_source_loads_document_without_metadata()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var app = builder.Build();
            app.MapGet("/config-db/service", () => Results.Text(
                "{\"_id\":\"service\",\"_rev\":\"1-abc\",\"Feature\":{\"Enabled\":true,\"Name\":\"x\"}}", "application/json"));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

            var configuration = new ConfigurationBuilder()
                .Add(new CouchDbConfigurationSource(new ServiceConfigurationOptions { Uri = new Uri(address + "/"), Database = "config-db", Document = "service" }))
                .Build();

            Assert.Equal("True", configuration["Feature:Enabled"]);
            Assert.Equal("x", configuration["Feature:Name"]);
            Assert.Null(configuration["_id"]);
            Assert.Null(configuration["_rev"]);
        }
    }
}
