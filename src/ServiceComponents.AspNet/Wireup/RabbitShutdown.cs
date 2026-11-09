using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Hosting;
using Serilog;
using ServiceComponents.Infrastructure.Rabbit;

namespace ServiceComponents.AspNet.Wireup;

// Registered by AddRabbit: when the host stops, the consumers stop taking messages and the deliveries being handled
// finish (within the host's shutdown timeout) before the container closes the channels.
internal sealed class RabbitShutdown : IHostedService
{
    private readonly ILogger _log;
    private readonly ILifetimeScope _scope;

    public RabbitShutdown(ILogger log, ILifetimeScope scope)
    {
        _log = log;
        _scope = scope;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var consumers = _scope.ResolveKeyed<IEnumerable<RabbitConsumer>>("__consumer__").ToArray();
        try {
            await Task.WhenAll(consumers.Select(consumer => consumer.DrainAsync(cancellationToken)));
        }
        catch (OperationCanceledException) {
            _log.Warning("RabbitMQ consumers were still handling messages at shutdown; the broker redelivers them");
        }
    }
}
