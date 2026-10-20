using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Orders.Service;
using ServiceComponents.Application.Senders;
using ServiceComponents.Testing;
using ServiceComponents.Testing.Extensions;

namespace Orders.Tests;

/// <summary>
/// Runs the real Orders host in memory. Published events are captured by a mock instead of going to RabbitMQ.
/// </summary>
public class OrdersTestHost : TestHost<Program>
{
    public Mock<IPublishEvent> EventPublisher { get; } = new();

    public override void ConfigureTestServices(IServiceCollection services)
    {
    }

    public override void ConfigureTestContainer(ContainerBuilder builder)
    {
        // Registered last, so it replaces the event router for every handler.
        builder.UseTestPublisher(EventPublisher.Object);
    }
}
