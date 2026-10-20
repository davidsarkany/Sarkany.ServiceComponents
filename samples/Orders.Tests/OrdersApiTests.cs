using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Orders.Api;
using ServiceComponents.Testing;
using ServiceComponents.Testing.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Orders.Tests;

public class OrdersApiTests : Test<OrdersTestHost>
{
    public OrdersApiTests(OrdersTestHost testHost, ITestOutputHelper outputHelper) : base(testHost, outputHelper)
    {
    }

    /// <summary>
    /// Posts a message the same way the HTTP senders do: JSON body plus the "domain-type" header.
    /// </summary>
    private async Task<HttpResponseMessage> Post(object message, string correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/generic") {
            Content = new StringContent(JsonConvert.SerializeObject(message), Encoding.UTF8, "application/json")
        };
        var type = message.GetType();
        request.Headers.Add("domain-type", $"{type.FullName}, {type.Assembly.GetName().Name}");
        if (correlationId != null) {
            request.Headers.Add("correlation-id", correlationId);
        }

        return await TestHost.CreateClient().SendAsync(request);
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Placing_an_order_publishes_OrderPlaced()
    {
        var orderId = NewId();

        var response = await Post(new PlaceOrder(orderId, "customer-1", "book", 2), "corr-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        TestHost.EventPublisher.Published<OrderPlaced>(e => e.OrderId == orderId && e.Quantity == 2);
    }

    [Fact]
    public async Task Order_can_be_queried()
    {
        var orderId = NewId();
        await Post(new PlaceOrder(orderId, "customer-2", "pen", 3));

        var response = await Post(new GetOrder(orderId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("pen", order["product"]!.Value<string>());
        Assert.Equal("Placed", order["status"]!.Value<string>());
    }

    [Fact]
    public async Task Duplicate_order_is_a_business_error()
    {
        var orderId = NewId();
        await Post(new PlaceOrder(orderId, "customer-3", "cup", 1));

        var response = await Post(new PlaceOrder(orderId, "customer-3", "cup", 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($"{{\"Type\":\"business\",\"ErrorCode\":0,\"ErrorMessage\":\"Order {orderId} already exists\"}}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invalid_order_is_a_validation_error()
    {
        var response = await Post(new PlaceOrder(NewId(), "customer-4", "", 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation", error["Type"]!.Value<string>());
        Assert.Equal(new[] { "Product", "Quantity" }, error["Errors"]!.Select(e => e["PropertyName"]!.Value<string>()));
    }

    [Fact]
    public async Task Cancelling_an_unknown_order_is_not_found()
    {
        var response = await Post(new CancelOrder(NewId()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancelling_publishes_OrderCancelled_once()
    {
        var orderId = NewId();
        await Post(new PlaceOrder(orderId, "customer-5", "lamp", 1));

        await Post(new CancelOrder(orderId));
        await Post(new CancelOrder(orderId));

        TestHost.EventPublisher.Published<OrderCancelled>(e => e.OrderId == orderId);
    }

    [Fact]
    public async Task Health_endpoints_respond()
    {
        var client = TestHost.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/.well-known/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/.well-known/ready")).StatusCode);
        Assert.Contains("generic_request_counter", await client.GetStringAsync("/metrics"));
    }
}
