| Scenario | Metric | Baseline | Current | Delta | |
|---|---|---:|---:|---:|---|
| http-command (PlaceOrder) | req/s | 37619.99 | 37657.40 | +0.1% | ✅ |
| http-command (PlaceOrder) | p50 ms | 0.70 | 0.71 | +1.4% |  |
| http-command (PlaceOrder) | p95 ms | 1.41 | 1.40 | -0.3% |  |
| http-command (PlaceOrder) | p99 ms | 3.77 | 2.68 | -28.7% | ✅ |
| http-query (GetOrder) | req/s | 53322.62 | 53497.44 | +0.3% | ✅ |
| http-query (GetOrder) | p50 ms | 0.55 | 0.55 | +0.9% |  |
| http-query (GetOrder) | p95 ms | 0.99 | 0.97 | -1.5% |  |
| http-query (GetOrder) | p99 ms | 1.19 | 1.16 | -2.8% | ✅ |

Thresholds: throughput may drop at most 5%, p99 may grow at most 10%. Medians of 4 baseline and 4 current run(s), measured alternately on SARKANY-PC (16 cores).
