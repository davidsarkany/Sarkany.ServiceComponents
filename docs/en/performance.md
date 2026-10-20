[Magyar](../hu/performance.md) | **English**

# Performance

The performance suite runs the same code against two builds on the same machine:

| Build | What it is | How it is built |
|---|---|---|
| **Baseline** | The original `ServiceComponents.*` packages, version `0.2.0-beta.12` (the last release before the rewrite) | restored from nuget.org |
| **Current** | The sources in this repository (project references) | |

`samples/` and `perf/` select the build with `-p:ScTarget=Baseline|Current` (see `samples/Directory.Build.props`). The results recorded below were measured during the rewrite against the same 0.2.0-beta.12 sources, packed locally.

## Running

```bash
bash perf/run.sh                       # full run, ~30-40 minutes
PERF_QUICK=1 bash perf/run.sh          # smoke test of the suite, noisy numbers
PERF_FILTER='*Mediator*' PERF_SCENARIOS=http bash perf/run.sh
```

The report is written to `perf/results/<timestamp>/README.md`. The exit code is non-zero when a load test exceeds the thresholds.

- Close other applications before a run.
- Compare runs only from the same machine.
- CI runners are too noisy for regression decisions.

## Micro benchmarks (`perf/ServiceComponents.Benchmarks`, BenchmarkDotNet)

Every benchmark runs for both builds in one session. Baseline is the ratio base, and memory allocations are measured.

| Class | Measures |
|---|---|
| `MediatorBenchmarks` | Command, query and event (2 handlers) through receiver, behaviors, dispatcher and handler, in a new lifetime scope. `Bare` uses dispatchers only; `Full` is the `UseDefault` + Prometheus pipeline. |
| `ParserBenchmarks` | `RequestParser` with a type name (small payload and 200 line items) and with an embedded `$type` |
| `RabbitBenchmarks` | Sending a command, publishing an event, consuming a command through the full pipeline. The `IChannel` fake completes immediately, so no broker I/O is involved. |
| `HttpBenchmarks` | Receiving a command or query on a `UseDefault` host via TestServer (MVC, parser, pipeline). HTTP sender to receiver round trip. |
| `StartupBenchmarks` | Building and starting a `UseDefault` host |

## Load tests (`perf/ServiceComponents.LoadTests`)

A closed-loop generator: N workers send requests back to back. A warmup comes first, then a measured run of 30 s (10 s with `--quick`). Exact percentiles are computed from every latency.

| Scenario | What runs |
|---|---|
| `http-command (PlaceOrder)` | The Orders sample on Kestrel: HTTP, parser, full pipeline, in-memory store, loopback event |
| `http-query (GetOrder)` | Same host, query returning JSON |
| `rabbit-command round trip` | Send through RabbitMQ (Testcontainers), consume, full pipeline, handle |
| `rabbit-query RPC` | Query RPC through RabbitMQ (queue named after the exchange), concurrency capped at 8 |

The generator is part of the repository instead of NBomber, because NBomber needs a license for organizational use.

## Regression thresholds

A rewritten package is accepted only when it stays within these limits against Baseline:

| Suite | Metric | Threshold |
|---|---|---|
| Micro benchmarks | mean time | at most +5% |
| | allocations | must not increase |
| Load tests | throughput | at most −5% |
| | p99 latency | at most +10% |

`perf/run.sh` checks the load test thresholds automatically. Read the micro benchmark ratios from the report.

**Deliberate costs are not regressions.** A fix or a security check may cost more than the old, wrong behavior. Such a cost is accepted if all of these hold:

- it is small in absolute terms;
- it is on a path that is not hot in production;
- it is listed in the table below and in the migration notes.

Do not add caches or other complexity only to get such a case under the threshold.

| Benchmark | Cost | Reason |
|---|---|---|
| Parser, embedded `$type` | +11% (about 78 ns on 0.7 µs) | Allowlist check of every `$type` (security fix). Senders never use this path. |
| Mediator event, Bare pipeline | +8% allocation (0.76 KB) | The child scope inherits the parent correlation (fix #1). With the real `UseDefault` pipeline allocations are 14% lower. |

## Results

All numbers are from 2026-10-19, measured on an AMD Ryzen 7 5700X (8 cores / 16 threads) with Windows 11 and .NET 10.0.7, before the rewrite. At that point Baseline and Current contain the same code, so every difference between them is measurement noise.

### Micro benchmarks (full run)

| Benchmark | Baseline mean | Allocated | Current / Baseline |
|---|---:|---:|---:|
| Mediator command, Bare | 1.9 µs | 6.1 KB | 0.98 |
| Mediator command, Full pipeline | 27.7 µs | 30.4 KB | 1.03 |
| Mediator query, Full pipeline | 22.7 µs | 25.9 KB | 0.99 |
| Mediator event (2 handlers), Full pipeline | 21.7 µs | 26.5 KB | 1.00 |
| Parser, typed small | 1.4 µs | 3.4 KB | 1.02 |
| Parser, typed 200 items | 90.6 µs | 65.4 KB | 0.96 |
| Parser, embedded `$type` | 0.7 µs | 3.4 KB | 1.01 |
| RabbitMQ send command | 4.8 µs | 10.2 KB | 1.00 |
| RabbitMQ consume command, full pipeline | 38.8 µs | 37.7 KB | 1.01 |
| HTTP receive command (TestServer) | 158 µs | 96 KB | 1.01 |
| HTTP send and receive query | 199 µs | 106 KB | 1.00 |
| Build and start `UseDefault` host | 5.5 ms | 3.2 MB | 1.03 |

**Noise floor:**

- Mean time stays within ±4% of the baseline.
- Allocations are identical, except a 2% difference in the HTTP receive path that comes from the different assembly versions of the two builds.

### Load tests

Each figure is the median of 4 alternating rounds, run with 32 workers for 30 s per round.

| Scenario | Throughput | p50 | p99 | Current vs Baseline |
|---|---:|---:|---:|---|
| HTTP command (Orders on Kestrel, full pipeline) | 37 600 req/s | 0.70 ms | 2.7–4.1 ms | throughput +0.1% |
| HTTP query | 53 300 req/s | 0.55 ms | 1.2 ms | throughput +0.3% |
| RabbitMQ command round trip | 18 200 msg/s | 1.7 ms | 3.0 ms | throughput −3.2% (single round) |
| RabbitMQ query RPC | 128 req/s | 62 ms | 69 ms | throughput +1.2% (single round); after the RabbitMQ rewrite −1.8% (noise) |

**Notes**

- **Use several rounds.** A single sequential A/B round differed by up to 7% in throughput, although the code was identical. That is why `perf/run.sh` runs 3 alternating rounds by default and compares the medians.
- **HTTP command p99 is noisy.** The p99 of the HTTP command scenario swings between 2.6 and 4.1 ms from round to round (GC). Judge it over more rounds, or look at p95.
- **RabbitMQ query RPC is slow.**
  - **Why:** every query declares, consumes and deletes its own reply queue on the shared publisher channel. These channel operations run one after another and take about 8 ms per query, so throughput stays at about 130 requests per second whatever the concurrency: one worker sees 8.6 ms, eight workers see 62 ms.
  - **What did not help:** waiting asynchronously instead of blocking (Sarkany) did not change this.
  - **What a compatible fix needs:** a pool of channels, each with a long-lived reply queue. A single shared reply queue does not work: concurrent queries of one request carry the same `CorrelationId`, and receivers running the original packages echo only that.
  - **Planned:** as an optional improvement after the rewrite.

### After rewriting Core, Domain and Infrastructure

All three benchmark suites were run in one full session; each ratio compares Current with Baseline.

| Benchmark | Current / Baseline | Allocated |
|---|---:|---:|
| Mediator command, Full pipeline | 0.46 | 0.84 |
| Mediator query, Full pipeline | 0.45 | 0.81 |
| Mediator event (2 handlers), Full pipeline | 0.44 | 0.86 |
| Mediator command, Bare | 0.80 | 0.98 |
| Mediator query, Bare | 0.83 | 0.97 |
| Mediator event (2 handlers), Bare | 0.91 | 1.08 |
| RabbitMQ consume command, full pipeline | 0.43 | 0.86 |
| RabbitMQ send command / publish event | 0.97 / 0.98 | 1.00 |
| Parser, typed small | 0.36 | 0.88 |
| Parser, typed 200 items | 0.99 | 0.99 |
| Parser, embedded `$type` | 1.05–1.11 | 1.00 |

The interop check passes in all four pairings.

### After rewriting AspNet

HTTP benchmarks and the HTTP load tests (3 alternating rounds, 32 workers), Current vs Baseline:

| Benchmark / scenario | Current / Baseline | Allocated |
|---|---:|---:|
| HTTP receive command / query | 0.81 / 0.79 | 0.94 |
| HTTP send command / query (sender + receiver) | 0.82 / 0.81 | 0.95 / 0.97 |
| Load: HTTP command | throughput +21.8%, p99 −14.7% | |
| Load: HTTP query | throughput +19.3%, p99 −10.2% | |

The integration packages (Redis, NHibernate, CouchDB, Host) are not on a measured path; their changes are covered by tests only.

### Final comparison (all packages rewritten)

Full run `perf/results/20261020-082855`: every benchmark, HTTP and RabbitMQ load tests, 3 alternating rounds. Ratios are Current / Baseline.

| Benchmark | Time | Allocated |
|---|---:|---:|
| Mediator command / query / event, full pipeline | 0.47 / 0.42 / 0.45 | 0.84 / 0.81 / 0.85 |
| Mediator command / query / event, bare | 0.85 / 0.85 / 0.88 | 0.98 / 0.97 / 1.08 |
| HTTP receive command / query | 0.79 / 0.77 | 0.94 / 0.92 |
| HTTP send command / query (sender + receiver) | 0.85 / 0.83 | 0.95 |
| RabbitMQ consume command, full pipeline | 0.44 | 0.85 |
| RabbitMQ send command / publish event | 0.99 / 1.04 | 1.00 |
| Parser typed small / 200 items / embedded `$type` | 0.34 / 0.97 / 1.02 | 0.88 / 0.99 / 1.00 |
| Build and start a `UseDefault` host | 0.94 | 0.98 |

| Load test | Throughput | p99 |
|---|---:|---:|
| HTTP command | +28.0% | −38.8% |
| HTTP query | +23.0% | −12.3% |
| RabbitMQ command round trip | +2.1% | −1.4% |
| RabbitMQ query RPC | −0.4% | +1.3% |

Everything is within the thresholds. The +8% allocation of the bare event dispatch and the cost of the `$type` allowlist check are the documented deliberate costs.

### Interoperability

`build/interop.sh` runs the Orders and Notifications samples in all four Baseline/Current pairings. In each pairing, Orders publishes `OrderPlaced` over RabbitMQ, and Notifications consumes it and queries Orders over HTTP. All pairings pass.
