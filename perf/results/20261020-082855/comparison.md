| Scenario | Metric | Baseline | Current | Delta | |
|---|---|---:|---:|---:|---|
| http-command (PlaceOrder) | req/s | 36958.43 | 47320.66 | +28.0% | ✅ |
| http-command (PlaceOrder) | p50 ms | 0.71 | 0.55 | -22.1% |  |
| http-command (PlaceOrder) | p95 ms | 1.45 | 1.23 | -15.1% |  |
| http-command (PlaceOrder) | p99 ms | 3.78 | 2.31 | -38.8% | ✅ |
| http-query (GetOrder) | req/s | 51774.45 | 63673.12 | +23.0% | ✅ |
| http-query (GetOrder) | p50 ms | 0.56 | 0.46 | -18.4% |  |
| http-query (GetOrder) | p95 ms | 1.00 | 0.91 | -9.5% |  |
| http-query (GetOrder) | p99 ms | 1.23 | 1.08 | -12.3% | ✅ |
| rabbit-command round trip | req/s | 18235.77 | 18625.48 | +2.1% | ✅ |
| rabbit-command round trip | p50 ms | 1.70 | 1.66 | -2.2% |  |
| rabbit-command round trip | p95 ms | 2.44 | 2.39 | -2.2% |  |
| rabbit-command round trip | p99 ms | 2.98 | 2.94 | -1.4% | ✅ |
| rabbit-query RPC | req/s | 130.70 | 130.23 | -0.4% | ✅ |
| rabbit-query RPC | p50 ms | 61.06 | 61.12 | +0.1% |  |
| rabbit-query RPC | p95 ms | 63.99 | 64.17 | +0.3% |  |
| rabbit-query RPC | p99 ms | 66.77 | 67.61 | +1.3% | ✅ |

Thresholds: throughput may drop at most 5%, p99 may grow at most 10%. Medians of 3 baseline and 3 current run(s), measured alternately on SARKANY-PC (16 cores).
