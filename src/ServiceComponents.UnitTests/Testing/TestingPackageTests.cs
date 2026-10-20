using System;
using System.Threading;
using Autofac;
using Microsoft.Extensions.Hosting;
using Moq;
using ServiceComponents.Application.Senders;
using ServiceComponents.Testing.Extensions;
using Xunit;
using Xunit.Sdk;

namespace ServiceComponents.UnitTests.Testing
{
    public class TestingPackageTests
    {
        [Fact]
        public void Published_verifies_event_count_and_predicate()
        {
            var publisher = new Mock<IPublishEvent>();
            publisher.Object.PublishAsync(new SampleEvent("a"));
            publisher.Object.PublishAsync(new SampleEvent("b"));

            publisher.Published<SampleEvent>(2);
            publisher.Published<SampleEvent>(e => e.Data == "a");
            publisher.Published<SampleEvent>(e => e.Data == "c", 0);
            Assert.Throws<MockException>(() => publisher.Published<SampleEvent>());
        }

        [Fact]
        public void UseTestPublisher_registers_instance()
        {
            var publisher = Mock.Of<IPublishEvent>();
            var builder = new ContainerBuilder();

            builder.UseTestPublisher(publisher);

            using var container = builder.Build();
            Assert.Same(publisher, container.Resolve<IPublishEvent>());
        }

        [Fact]
        public void UseOutputHelper_requires_output_helper()
        {
            var error = Assert.Throws<ArgumentNullException>(() => new HostBuilder().UseOutputHelper(null));

            Assert.StartsWith("Set TestHost.OutputHelper property", error.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("http://127.0.0.1:1")]
        [InlineData(null)]
        public void UseOutputHelper_writes_to_the_output_helper_with_configurable_seq(string seqServerUrl)
        {
            var output = new Mock<Xunit.Abstractions.ITestOutputHelper>();
            var settings = new System.Collections.Generic.Dictionary<string, string>();
            if (seqServerUrl != null) {
                settings["ServiceComponents:Testing:SeqServerUrl"] = seqServerUrl;
            }

            using var host = new HostBuilder()
                .ConfigureAppConfiguration(configuration => Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(configuration, settings))
                .UseOutputHelper(output.Object)
                .Build();
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Serilog.ILogger>(host.Services).Information("hello {name}", "test");

            output.Verify(o => o.WriteLine(It.Is<string>(line => line.Contains("hello test"))), Times.Once);
        }
    }
}
