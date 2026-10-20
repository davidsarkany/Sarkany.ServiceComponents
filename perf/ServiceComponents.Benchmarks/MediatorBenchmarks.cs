using Autofac;
using BenchmarkDotNet.Attributes;
using ServiceComponents.Infrastructure.Receivers;

namespace ServiceComponents.Benchmarks;

/// <summary>
/// One request through receiver, behaviors, dispatcher and handler, in its own lifetime scope as a transport does it.
/// </summary>
[MemoryDiagnoser]
public class MediatorBenchmarks
{
    private IContainer _container;
    private readonly BenchCommand _command = new("name", 5);
    private readonly BenchQuery _query = new(42);
    private readonly BenchEvent _event = new("event");

    [Params(Pipeline.Bare, Pipeline.Full)]
    public Pipeline Pipeline { get; set; }

    [GlobalSetup]
    public void Setup() => _container = Containers.Mediator(Pipeline).Build();

    [GlobalCleanup]
    public void Cleanup() => _container.Dispose();

    [Benchmark]
    public async Task Command()
    {
        await using var scope = _container.BeginLifetimeScope();
        await scope.Resolve<IReceiveCommand>().ReceiveAsync(_command);
    }

    [Benchmark]
    public async Task<BenchResult> Query()
    {
        await using var scope = _container.BeginLifetimeScope();
        return await scope.Resolve<IReceiveQuery>().ReceiveAsync(_query);
    }

    [Benchmark(Description = "Event (2 handlers)")]
    public async Task Event()
    {
        await using var scope = _container.BeginLifetimeScope();
        await scope.Resolve<IReceiveEvent>().ReceiveAsync(_event);
    }
}
