| Scenario | Metric | Baseline | Current | Delta | |
|---|---|---:|---:|---:|---|
| http-command (PlaceOrder) | req/s | 38924.42 | 36099.29 | -7.3% | ❌ |
| http-command (PlaceOrder) | p50 ms | 0.68 | 0.72 | +6.0% |  |
| http-command (PlaceOrder) | p95 ms | 1.35 | 1.48 | +9.2% |  |
| http-command (PlaceOrder) | p99 ms | 3.41 | 3.98 | +16.9% | ❌ |
| http-query (GetOrder) | req/s | 55004.08 | 51193.40 | -6.9% | ❌ |
| http-query (GetOrder) | p50 ms | 0.54 | 0.57 | +6.2% |  |
| http-query (GetOrder) | p95 ms | 0.91 | 1.03 | +13.1% |  |
| http-query (GetOrder) | p99 ms | 1.13 | 1.23 | +9.2% | ✅ |
| rabbit-command round trip | req/s | 18184.23 | 17599.99 | -3.2% | ✅ |
| rabbit-command round trip | p50 ms | 1.70 | 1.75 | +2.9% |  |
| rabbit-command round trip | p95 ms | 2.44 | 2.56 | +5.0% |  |
| rabbit-command round trip | p99 ms | 2.96 | 3.11 | +4.9% | ✅ |
| rabbit-query RPC | req/s | 127.63 | 129.22 | +1.2% | ✅ |
| rabbit-query RPC | p50 ms | 62.44 | 61.75 | -1.1% |  |
| rabbit-query RPC | p95 ms | 65.28 | 64.68 | -0.9% |  |
| rabbit-query RPC | p99 ms | 69.14 | 70.45 | +1.9% | ✅ |

Thresholds: throughput may drop at most 5%, p99 may grow at most 10%. Baseline measured on SARKANY-PC (16 cores) at 2026-10-19 19:10:36Z, current at 2026-10-19 19:13:26Z.
