using System.Text;
using Autofac;
using BenchmarkDotNet.Attributes;
using Newtonsoft.Json;
using RabbitMQ.Client;
using ServiceComponents.Application.Senders;
using ServiceComponents.Infrastructure.Rabbit;

namespace ServiceComponents.Benchmarks;

/// <summary>
/// RabbitMQ transport overhead without the broker: sender side (correlation, serialization, properties) and consumer
/// side (parsing, correlation, full pipeline, ack).
/// </summary>
[MemoryDiagnoser]
public class RabbitBenchmarks
{
    private IContainer _container;
    private FakeChannel _channel;
    private ReadOnlyMemory<byte> _commandBody;
    private BasicProperties _commandProperties;
    private readonly BenchCommand _command = new("name", 5);
    private readonly BenchEvent _event = new("event");
    private ulong _deliveryTag;

    [GlobalSetup]
    public void Setup()
    {
        var (channel, fake) = FakeChannel.Create();
        _channel = fake;
        var builder = Containers.Mediator(Pipeline.Full);
        builder.RegisterInstance(channel).As<IChannel>();
        builder.AddRabbitReceivers();
        builder.AddRabbitReceiverCorrelationBehavior();
        builder.AddRabbitSenderCorrelationBehavior();
        builder.AddRabbitCommandSender("bench-exchange", "");
        builder.AddRabbitEventPublisher("bench-exchange", "");
        builder.AddRabbitConsumer("bench-queue", "bench-consumer");
        _container = builder.Build();
        _container.Resolve<RabbitConsumer>().StartAsync().GetAwaiter().GetResult();

        _commandBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(_command));
        _commandProperties = new BasicProperties {
            Type = Payloads.TypeName(typeof(BenchCommand)),
            CorrelationId = "corr",
            Headers = new Dictionary<string, object> { ["causation_id"] = Encoding.UTF8.GetBytes("cause") }
        };
    }

    [GlobalCleanup]
    public void Cleanup() => _container.Dispose();

    [Benchmark(Description = "Send command")]
    public async Task SendCommand()
    {
        await using var scope = _container.BeginLifetimeScope();
        await scope.Resolve<ISendCommand>().SendAsync(_command);
    }

    [Benchmark(Description = "Publish event")]
    public async Task PublishEvent()
    {
        await using var scope = _container.BeginLifetimeScope();
        await scope.Resolve<IPublishEvent>().PublishAsync(_event);
    }

    [Benchmark(Description = "Consume command (full pipeline)")]
    public Task ConsumeCommand() =>
        _channel.Consumer.HandleBasicDeliverAsync("bench-consumer", ++_deliveryTag, false, "bench-exchange", "", _commandProperties, _commandBody);
}
