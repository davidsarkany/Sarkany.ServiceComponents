using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace ServiceComponents.Benchmarks;

public static class Program
{
    /// <summary>
    /// Runs every benchmark against the Baseline (pre-rewrite NuGet packages, marked as baseline) and the Current
    /// (repository sources) build. Set PERF_QUICK=1 for a short, less precise run.
    /// Usage: dotnet run -c Release -- --filter '*'   (see perf/run.sh)
    /// </summary>
    public static void Main(string[] args)
    {
        var quick = Environment.GetEnvironmentVariable("PERF_QUICK") == "1";
        var job = quick ? Job.ShortRun : Job.Default;

        var config = DefaultConfig.Instance
            .AddJob(job.WithArguments(new[] { new MsBuildArgument("/p:ScTarget=Baseline") }).WithId("Baseline").AsBaseline())
            .AddJob(job.WithArguments(new[] { new MsBuildArgument("/p:ScTarget=Current") }).WithId("Current"))
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(JsonExporter.FullCompressed, MarkdownExporter.GitHub)
            .HideColumns("Arguments")
            .WithOptions(ConfigOptions.JoinSummary);

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
