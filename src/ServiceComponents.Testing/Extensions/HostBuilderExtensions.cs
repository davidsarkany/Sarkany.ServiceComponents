using System;
using Microsoft.Extensions.Hosting;
using Serilog;
using Xunit.Abstractions;

namespace ServiceComponents.Testing.Extensions
{
    public static class HostBuilderExtensions
    {
        private const string SeqServerUrlKey = "ServiceComponents:Testing:SeqServerUrl";
        private const string DefaultSeqServerUrl = "http://localhost:5341";

        public static IHostBuilder UseOutputHelper(this IHostBuilder builder, ITestOutputHelper outputHelper)
        {
            if (outputHelper is null) {
                throw new ArgumentNullException(nameof(outputHelper), "Set TestHost.OutputHelper property");
            }

            return builder.UseSerilog((context, configuration) => {
                configuration.ReadFrom.Configuration(context.Configuration);

                // Kept for compatibility: a local Seq by default. Set the key to another URL, or to "" to disable it.
                var seqServerUrl = context.Configuration[SeqServerUrlKey] ?? DefaultSeqServerUrl;
                if (!string.IsNullOrWhiteSpace(seqServerUrl)) {
                    configuration.WriteTo.Seq(seqServerUrl);
                }

                configuration.WriteTo.TestOutput(outputHelper).MinimumLevel.Verbose();
            });
        }
    }
}
