using Autofac;
using Microsoft.Extensions.Configuration;

using RabbitMQ.Client;

namespace ServiceComponents.Infrastructure.Rabbit;

/// <summary>
/// Messaging behavior of the RabbitMQ senders and consumers. Every option defaults to the behavior of the original packages.
/// Read from a registered <see cref="RabbitMessagingOptions"/> instance, otherwise from the
/// <see cref="ConfigurationSectionName"/> configuration section.
/// </summary>
public class RabbitMessagingOptions
{
    public const string ConfigurationSectionName = "ServiceComponents:RabbitMessagingOptions";

    /// <summary>
    /// Time after which a query sent over RabbitMQ fails with <see cref="System.TimeoutException"/>.
    /// Default: none, the caller waits until its cancellation token is cancelled.
    /// </summary>
    public double? QueryTimeoutInSeconds { get; set; }

    /// <summary>
    /// When a query handler fails, the consumer also sends an error reply (headers <c>x-sc-error-*</c>), so the caller
    /// gets an exception instead of waiting. Default: false. <b>Enable only when every sender runs Sarkany.ServiceComponents</b>:
    /// original senders would deserialize the error reply as the query result.
    /// </summary>
    public bool ReplyOnQueryFailure { get; set; }

    /// <summary>
    /// Maximum number of unacknowledged messages per consumer (basic.qos). Default: 0, no limit.
    /// </summary>
    public ushort PrefetchCount { get; set; }

    /// <summary>
    /// Channels are opened with publisher confirms: publishing a command, event or query completes when the broker has
    /// taken the message, and fails otherwise (also for unroutable <c>mandatory</c> messages). Default: false, publishing
    /// does not wait for the broker. Nothing changes on the wire for the receivers.
    /// </summary>
    public bool PublisherConfirms { get; set; }

    internal CreateChannelOptions ChannelOptions => PublisherConfirms ? new CreateChannelOptions(true, true) : null;

    internal static RabbitMessagingOptions Resolve(IComponentContext context)
    {
        if (context.TryResolve<RabbitMessagingOptions>(out var registered)) {
            return registered;
        }

        var options = new RabbitMessagingOptions();
        context.ResolveOptional<IConfiguration>()?.GetSection(ConfigurationSectionName).Bind(options);
        return options;
    }
}
