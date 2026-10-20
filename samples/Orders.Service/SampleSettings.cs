using Microsoft.Extensions.Configuration;

namespace Orders.Service;

/// <summary>
/// RabbitMQ settings of the sample. They are read before the host is built because
/// <c>AddRabbit</c> registers the broker topology while the host builder is being set up.
/// </summary>
public class RabbitSettings
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = "amqp://guest:guest@localhost:5672";
    public string ClientName { get; set; } = "orders";
    public string Queue { get; set; } = "orders";
    public string Exchange { get; set; } = "orders-events";
    // No default array here: the configuration binder appends to existing arrays instead of replacing them,
    // which would turn [1000, 5000] into [1000, 5000, 1000, 5000] and declare conflicting retry queues.
    public int[] RetryIntervals { get; set; }

    public static RabbitSettings Load(string[] args, string section)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args ?? Array.Empty<string>())
            .Build();
        var settings = configuration.GetSection(section).Get<RabbitSettings>() ?? new RabbitSettings();
        settings.RetryIntervals ??= new[] { 1000, 5000 };
        return settings;
    }
}
