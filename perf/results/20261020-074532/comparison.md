| Scenario | Metric | Baseline | Current | Delta | |
|---|---|---:|---:|---:|---|
| rabbit-command round trip | req/s | 18371.79 | 18783.97 | +2.2% | ✅ |
| rabbit-command round trip | p50 ms | 1.68 | 1.65 | -2.2% |  |
| rabbit-command round trip | p95 ms | 2.42 | 2.36 | -2.4% |  |
| rabbit-command round trip | p99 ms | 2.92 | 2.84 | -2.7% | ✅ |
| rabbit-query RPC | req/s | 131.99 | 129.56 | -1.8% | ✅ |
| rabbit-query RPC | p50 ms | 60.27 | 61.42 | +1.9% |  |
| rabbit-query RPC | p95 ms | 63.93 | 65.07 | +1.8% |  |
| rabbit-query RPC | p99 ms | 67.23 | 68.82 | +2.4% | ✅ |

Thresholds: throughput may drop at most 5%, p99 may grow at most 10%. Medians of 2 baseline and 2 current run(s), measured alternately on SARKANY-PC (16 cores).
