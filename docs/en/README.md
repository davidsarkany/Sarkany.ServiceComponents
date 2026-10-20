[Magyar](../hu/README.md) | **English**

# ServiceComponents documentation

ServiceComponents is a set of .NET libraries for building message-driven microservices. You write commands, queries, events and their handlers. The libraries take care of the rest:

- receiving the messages over HTTP, RabbitMQ or in-process;
- running them through a pipeline (validation, logging, correlation, metrics);
- routing outgoing messages to the right transport.

| Topic | |
|---|---|
| [Overview](overview.md) | Packages, architecture, request flow |
| [Getting started](getting-started.md) | Build a service step by step, based on the sample |
| [Messaging concepts](concepts/messaging.md) | Commands, queries, events, handlers, hooks, routing, correlation |
| [HTTP transport](transports/http.md) | Generic endpoint, headers, error contract, HTTP senders |
| [RabbitMQ transport](transports/rabbitmq.md) | Topology, retry and dead lettering, query RPC, connection options |
| [Observability](observability.md) | Logging, metrics, health checks |
| [Configuration](configuration.md) | Every options class, its section and defaults |
| [Integrations](integrations.md) | Redis, NHibernate, CouchDB |
| [Testing](testing.md) | `ServiceComponents.Testing`, the repository's test suites |
| [Wire protocol reference](reference/wire-protocol.md) | The normative description of everything other services depend on |
| [Known issues](known-issues.md) | Defects of the current version, pinned by tests |
| [Performance](performance.md) | Benchmarks, load tests, baseline comparison |

## Samples

[`samples/`](../../samples) contains two services built only on the public API:

- **Orders** receives commands and queries over HTTP and publishes events to RabbitMQ.
- **Notifications** consumes those events and queries Orders over HTTP.

Start everything with:

```bash
docker compose -f samples/docker-compose.yml up --build
```

The code snippets in this documentation come from the samples.
