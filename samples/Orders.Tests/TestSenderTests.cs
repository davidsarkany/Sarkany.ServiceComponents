#if !SC_BASELINE
// UseTestSender failed in 0.2: the test host it creates has no output helper.
using Autofac;
using Microsoft.Extensions.Options;
using Orders.Api;
using Serilog;
using ServiceComponents.AspNet.Exceptions;
using ServiceComponents.AspNet.Http.Senders;
using ServiceComponents.Testing.Extensions;
using Xunit;
using HttpRequestOptions = ServiceComponents.AspNet.Http.HttpRequestOptions;

namespace Orders.Tests;

/// <summary>
/// A client of the Orders service (e.g. Notifications) can send to an in-memory Orders host with UseTestSender.
/// </summary>
public class TestSenderTests
{
    [Fact]
    public async Task Senders_reach_the_test_host()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(new LoggerConfiguration().CreateLogger()).As<ILogger>();
        builder.RegisterInstance(Options.Create(new HttpRequestOptions()));
        builder.RegisterType<JsonExceptionMapper>().As<IExceptionMapperService>();
        builder.UseTestSender<OrdersTestHost>();
        await using var container = builder.Build();

        var orderId = Guid.NewGuid().ToString("N");
        await container.Resolve<ISendHttpCommand>().SendAsync(new PlaceOrder(orderId, "customer-5", "lamp", 1), new Dictionary<string, string>());
        var order = await container.Resolve<ISendHttpQuery>().SendAsync(new GetOrder(orderId), new Dictionary<string, string>());

        Assert.Equal("lamp", order.Product);
    }
}
#endif
