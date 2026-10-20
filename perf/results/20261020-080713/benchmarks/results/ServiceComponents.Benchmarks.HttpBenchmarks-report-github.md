```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
AMD Ryzen 7 5700X 3.40GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.203
  [Host]   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Baseline : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Current  : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method                             | Job      | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------------------- |--------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| &#39;Receive command&#39;                  | Baseline | 138.6 μs | 1.68 μs | 1.49 μs |  1.00 |    0.01 | 5.8594 |  96.34 KB |        1.00 |
| &#39;Receive command&#39;                  | Current  | 112.8 μs | 1.41 μs | 1.79 μs |  0.81 |    0.02 | 4.8828 |  90.93 KB |        0.94 |
|                                    |          |          |         |         |       |         |        |           |             |
| &#39;Receive query&#39;                    | Baseline | 141.7 μs | 1.57 μs | 1.47 μs |  1.00 |    0.01 | 4.8828 |  93.51 KB |        1.00 |
| &#39;Receive query&#39;                    | Current  | 112.1 μs | 1.38 μs | 1.22 μs |  0.79 |    0.01 | 5.3711 |  87.92 KB |        0.94 |
|                                    |          |          |         |         |       |         |        |           |             |
| &#39;Send command (sender + receiver)&#39; | Baseline | 162.5 μs | 2.41 μs | 2.25 μs |  1.00 |    0.02 | 5.8594 | 106.27 KB |        1.00 |
| &#39;Send command (sender + receiver)&#39; | Current  | 133.4 μs | 2.58 μs | 3.62 μs |  0.82 |    0.02 | 5.8594 | 100.68 KB |        0.95 |
|                                    |          |          |         |         |       |         |        |           |             |
| &#39;Send query (sender + receiver)&#39;   | Baseline | 163.4 μs | 1.35 μs | 1.26 μs |  1.00 |    0.01 | 5.8594 | 106.24 KB |        1.00 |
| &#39;Send query (sender + receiver)&#39;   | Current  | 132.6 μs | 1.70 μs | 1.59 μs |  0.81 |    0.01 | 5.8594 | 102.74 KB |        0.97 |
