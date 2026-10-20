using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace ServiceComponents.LoadTests;

/// <summary>
///   run     --target Baseline|Current --out results.json [--scenarios http,rabbit] [--quick]
///   compare --baseline a.json[,b.json…] --current c.json[,d.json…] --out comparison.md
///           (several runs per build are reduced to their median per scenario)
///           [--max-throughput-drop 5] [--max-p99-increase 10]
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        var options = Parse(args.Skip(1).ToArray());
        return args.FirstOrDefault() switch {
            "run" => await RunScenarios(options),
            "compare" => Compare(options),
            _ => Usage()
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: run --target <name> --out <file> [--scenarios http,rabbit] [--quick] | compare --baseline <file> --current <file> --out <file>");
        return 2;
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++) {
            var key = args[i].TrimStart('-');
            options[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
        }

        return options;
    }

    private static async Task<int> RunScenarios(Dictionary<string, string> options)
    {
        var quick = options.ContainsKey("quick");
        var settings = new ScenarioSettings(
            Concurrency: int.Parse(options.GetValueOrDefault("concurrency", "32")),
            Warmup: TimeSpan.FromSeconds(quick ? 3 : 10),
            Duration: TimeSpan.FromSeconds(quick ? 10 : 30));
        var scenarios = options.GetValueOrDefault("scenarios", "http,rabbit").Split(',');

        var results = new List<ScenarioResult>();
        if (scenarios.Contains("http")) {
            results.AddRange(await Scenarios.Http(settings));
        }

        if (scenarios.Contains("rabbit")) {
            results.AddRange(await Scenarios.Rabbit(settings));
        }

        var report = new LoadReport(options.GetValueOrDefault("target", "unknown"), Environment.MachineName, Environment.ProcessorCount, DateTime.UtcNow, results);
        var output = options.GetValueOrDefault("out", "load-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, JsonConvert.SerializeObject(report, Formatting.Indented));
        Console.WriteLine($"Results written to {output}");
        return 0;
    }

    private record Summary(string Machine, int ProcessorCount, int Runs, List<ScenarioResult> Results);

    /// <summary>Reads one or more reports (comma separated) and keeps the median of every metric per scenario.</summary>
    private static Summary Median(string files)
    {
        var reports = files.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(file => JsonConvert.DeserializeObject<LoadReport>(File.ReadAllText(file))).ToList();

        static double Med(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        }

        var results = reports[0].Results.Select(first => {
            var runs = reports.Select(r => r.Results.Single(x => x.Name == first.Name)).ToList();
            return first with {
                Requests = (long)Med(runs.Select(r => (double)r.Requests)),
                Errors = runs.Max(r => r.Errors),
                RequestsPerSecond = Med(runs.Select(r => r.RequestsPerSecond)),
                MeanMs = Med(runs.Select(r => r.MeanMs)),
                P50Ms = Med(runs.Select(r => r.P50Ms)),
                P95Ms = Med(runs.Select(r => r.P95Ms)),
                P99Ms = Med(runs.Select(r => r.P99Ms)),
                MaxMs = Med(runs.Select(r => r.MaxMs))
            };
        }).ToList();
        return new Summary(reports[0].Machine, reports[0].ProcessorCount, reports.Count, results);
    }

    private static int Compare(Dictionary<string, string> options)
    {
        var baseline = Median(options["baseline"]);
        var current = Median(options["current"]);
        var maxThroughputDrop = double.Parse(options.GetValueOrDefault("max-throughput-drop", "5"));
        var maxP99Increase = double.Parse(options.GetValueOrDefault("max-p99-increase", "10"));

        var markdown = new StringBuilder();
        markdown.AppendLine("| Scenario | Metric | Baseline | Current | Delta | |");
        markdown.AppendLine("|---|---|---:|---:|---:|---|");
        var failed = false;

        foreach (var b in baseline.Results) {
            var c = current.Results.SingleOrDefault(r => r.Name == b.Name);
            if (c == null) {
                markdown.AppendLine($"| {b.Name} | | | missing | | ❌ |");
                failed = true;
                continue;
            }

            void Row(string metric, double before, double after, bool higherIsBetter, double? limit)
            {
                var delta = (after - before) / before * 100;
                var regression = limit.HasValue && (higherIsBetter ? -delta > limit : delta > limit);
                failed |= regression;
                var mark = regression ? "❌" : limit.HasValue ? "✅" : "";
                markdown.AppendLine($"| {b.Name} | {metric} | {before:F2} | {after:F2} | {delta:+0.0;-0.0;0.0}% | {mark} |");
            }

            Row("req/s", b.RequestsPerSecond, c.RequestsPerSecond, true, maxThroughputDrop);
            Row("p50 ms", b.P50Ms, c.P50Ms, false, null);
            Row("p95 ms", b.P95Ms, c.P95Ms, false, null);
            Row("p99 ms", b.P99Ms, c.P99Ms, false, maxP99Increase);
            if (c.Errors > b.Errors) {
                markdown.AppendLine($"| {b.Name} | errors | {b.Errors} | {c.Errors} | | ❌ |");
                failed = true;
            }
        }

        markdown.AppendLine();
        markdown.AppendLine($"Thresholds: throughput may drop at most {maxThroughputDrop}%, p99 may grow at most {maxP99Increase}%. " +
                            $"Medians of {baseline.Runs} baseline and {current.Runs} current run(s), measured alternately on {baseline.Machine} ({baseline.ProcessorCount} cores).");

        File.WriteAllText(options.GetValueOrDefault("out", "load-comparison.md"), markdown.ToString());
        Console.WriteLine(markdown);
        return failed ? 1 : 0;
    }
}

public record LoadReport(string Target, string Machine, int ProcessorCount, DateTime Timestamp, List<ScenarioResult> Results);
