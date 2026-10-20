using System.Collections.Concurrent;
using System.Linq;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ServiceComponents.UnitTests.Fixtures
{
    /// <summary>
    /// Serilog logger that keeps every event in memory (verbose level, log context enrichment enabled).
    /// </summary>
    public class TestLog : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new ConcurrentQueue<LogEvent>();

        public ILogger Logger { get; }

        public TestLog()
        {
            Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.FromLogContext()
                .WriteTo.Sink(this)
                .CreateLogger();
        }

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);

        public string[] Templates => Events.Select(e => e.MessageTemplate.Text).ToArray();

        public LogEvent Single(string template) => Events.Single(e => e.MessageTemplate.Text == template);
    }
}
