using System.Diagnostics;

namespace ServiceComponents.LoadTests;

public record ScenarioSettings(int Concurrency, TimeSpan Warmup, TimeSpan Duration);

public record ScenarioResult(
    string Name,
    int Concurrency,
    double DurationSeconds,
    long Requests,
    long Errors,
    double RequestsPerSecond,
    double MeanMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs);

/// <summary>
/// Closed-loop load generator: <c>Concurrency</c> workers send requests back to back for a fixed time after a warmup.
/// Latencies of successful requests are kept in full to compute exact percentiles.
/// </summary>
public static class LoadDriver
{
    // Shared by warmup and measured runs so request numbers (used e.g. as order ids) never repeat.
    private static long _sequence;

    public static async Task<ScenarioResult> Run(string name, ScenarioSettings settings, Func<long, Task> request)
    {
        Console.WriteLine($"[{name}] warmup {settings.Warmup.TotalSeconds}s, run {settings.Duration.TotalSeconds}s, concurrency {settings.Concurrency}");
        await Execute(settings.Concurrency, settings.Warmup, request, record: false);
        var (latencies, errors, elapsed) = await Execute(settings.Concurrency, settings.Duration, request, record: true);

        latencies.Sort();
        double Percentile(double p) => latencies.Count == 0 ? double.NaN : latencies[Math.Min(latencies.Count - 1, (int)Math.Ceiling(p * latencies.Count) - 1)];

        var result = new ScenarioResult(
            name,
            settings.Concurrency,
            elapsed.TotalSeconds,
            latencies.Count + errors,
            errors,
            latencies.Count / elapsed.TotalSeconds,
            latencies.Count == 0 ? double.NaN : latencies.Average(),
            Percentile(0.50),
            Percentile(0.95),
            Percentile(0.99),
            latencies.Count == 0 ? double.NaN : latencies[^1]);
        Console.WriteLine($"[{name}] {result.RequestsPerSecond:F0} req/s, p50 {result.P50Ms:F2} ms, p95 {result.P95Ms:F2} ms, p99 {result.P99Ms:F2} ms, errors {result.Errors}");
        return result;
    }

    private static async Task<(List<double> latencies, long errors, TimeSpan elapsed)> Execute(int concurrency, TimeSpan duration, Func<long, Task> request, bool record)
    {
        var perWorker = Enumerable.Range(0, concurrency).Select(_ => new List<double>(4096)).ToArray();
        long errors = 0;
        var total = Stopwatch.StartNew();
        var deadline = TimeSpan.FromTicks(duration.Ticks);

        var workers = Enumerable.Range(0, concurrency).Select(worker => Task.Run(async () => {
            var latencies = perWorker[worker];
            while (total.Elapsed < deadline) {
                var start = Stopwatch.GetTimestamp();
                try {
                    await request(Interlocked.Increment(ref _sequence));
                    if (record) {
                        latencies.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    }
                }
                catch {
                    if (record) {
                        Interlocked.Increment(ref errors);
                    }
                }
            }
        })).ToArray();

        await Task.WhenAll(workers);
        return (perWorker.SelectMany(l => l).ToList(), errors, total.Elapsed);
    }
}
