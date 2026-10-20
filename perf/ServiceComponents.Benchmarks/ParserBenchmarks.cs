using BenchmarkDotNet.Attributes;
using Newtonsoft.Json;
using ServiceComponents.Infrastructure.Receivers;

namespace ServiceComponents.Benchmarks;

/// <summary>Deserialization of incoming payloads, shared by the HTTP and RabbitMQ receivers.</summary>
[MemoryDiagnoser]
public class ParserBenchmarks
{
    private readonly RequestParser _parser = new();
    private string _small;
    private string _large;
    private string _embedded;
    private string _smallType;
    private string _largeType;

    [GlobalSetup]
    public void Setup()
    {
        _small = JsonConvert.SerializeObject(new BenchCommand("name", 5));
        _large = JsonConvert.SerializeObject(Payloads.Large(200));
        _embedded = JsonConvert.SerializeObject(new BenchCommand("name", 5), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All });
        _smallType = Payloads.TypeName(typeof(BenchCommand));
        _largeType = Payloads.TypeName(typeof(LargeCommand));
    }

    private Task Parse(string payload, string type) =>
        _parser.ParseAsync(payload, type, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);

    [Benchmark(Description = "Typed (small)")]
    public Task Small() => Parse(_small, _smallType);

    [Benchmark(Description = "Typed (200 items)")]
    public Task Large() => Parse(_large, _largeType);

    [Benchmark(Description = "Embedded $type")]
    public Task Embedded() => Parse(_embedded, null);
}
