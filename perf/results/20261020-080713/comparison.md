| Scenario | Metric | Baseline | Current | Delta | |
|---|---|---:|---:|---:|---|
| http-command (PlaceOrder) | req/s | 37399.42 | 45564.52 | +21.8% | ✅ |
| http-command (PlaceOrder) | p50 ms | 0.69 | 0.55 | -20.2% |  |
| http-command (PlaceOrder) | p95 ms | 1.41 | 1.26 | -10.5% |  |
| http-command (PlaceOrder) | p99 ms | 4.40 | 3.75 | -14.7% | ✅ |
| http-query (GetOrder) | req/s | 53198.88 | 63443.31 | +19.3% | ✅ |
| http-query (GetOrder) | p50 ms | 0.55 | 0.45 | -18.1% |  |
| http-query (GetOrder) | p95 ms | 1.04 | 0.88 | -15.0% |  |
| http-query (GetOrder) | p99 ms | 1.22 | 1.10 | -10.2% | ✅ |

Thresholds: throughput may drop at most 5%, p99 may grow at most 10%. Medians of 3 baseline and 3 current run(s), measured alternately on SARKANY-PC (16 cores).
