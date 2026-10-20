```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
AMD Ryzen 7 5700X 3.40GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.203
  [Host]   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Baseline : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Current  : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Type               | Method                             | Job      | Pipeline | Mean           | Error         | StdDev        | Median         | Ratio | RatioSD | Gen0     | Gen1    | Allocated  | Alloc Ratio |
|------------------- |----------------------------------- |--------- |--------- |---------------:|--------------:|--------------:|---------------:|------:|--------:|---------:|--------:|-----------:|------------:|
| **HttpBenchmarks**     | **&#39;Receive command&#39;**                  | **Baseline** | **?**        |   **158,372.5 ns** |   **2,072.53 ns** |   **4,186.62 ns** |   **157,418.5 ns** |  **1.00** |    **0.04** |   **5.8594** |       **-** |   **96.34 KB** |        **1.00** |
| HttpBenchmarks     | &#39;Receive command&#39;                  | Current  | ?        |   159,508.1 ns |   1,332.88 ns |   1,779.35 ns |   159,306.4 ns |  1.01 |    0.03 |   5.8594 |       - |   98.47 KB |        1.02 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| ParserBenchmarks   | &#39;Typed (small)&#39;                    | Baseline | ?        |     1,389.1 ns |      12.68 ns |       9.90 ns |     1,388.7 ns |  1.00 |    0.01 |   0.2079 |  0.0019 |    3.41 KB |        1.00 |
| ParserBenchmarks   | &#39;Typed (small)&#39;                    | Current  | ?        |     1,415.5 ns |      10.07 ns |       7.87 ns |     1,416.7 ns |  1.02 |    0.01 |   0.2079 |  0.0019 |    3.41 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| RabbitBenchmarks   | &#39;Send command&#39;                     | Baseline | ?        |     4,763.2 ns |      47.01 ns |      36.70 ns |     4,755.5 ns |  1.00 |    0.01 |   0.6104 |       - |   10.18 KB |        1.00 |
| RabbitBenchmarks   | &#39;Send command&#39;                     | Current  | ?        |     4,766.1 ns |      52.67 ns |      41.12 ns |     4,772.2 ns |  1.00 |    0.01 |   0.6104 |       - |   10.18 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| StartupBenchmarks  | &#39;Build and start UseDefault host&#39;  | Baseline | ?        | 5,527,613.8 ns |  58,901.58 ns |  91,702.68 ns | 5,503,862.5 ns |  1.00 |    0.02 | 187.5000 | 15.6250 | 3200.96 KB |        1.00 |
| StartupBenchmarks  | &#39;Build and start UseDefault host&#39;  | Current  | ?        | 5,700,553.9 ns | 110,965.41 ns | 185,398.30 ns | 5,638,230.5 ns |  1.03 |    0.04 | 187.5000 | 15.6250 | 3228.28 KB |        1.01 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| HttpBenchmarks     | &#39;Receive query&#39;                    | Baseline | ?        |   160,987.5 ns |   1,448.90 ns |   2,685.64 ns |   160,469.2 ns |  1.00 |    0.02 |   4.8828 |       - |   93.47 KB |        1.00 |
| HttpBenchmarks     | &#39;Receive query&#39;                    | Current  | ?        |   159,656.5 ns |   1,240.61 ns |   1,656.18 ns |   159,341.9 ns |  0.99 |    0.02 |   4.8828 |       - |   93.62 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| ParserBenchmarks   | &#39;Typed (200 items)&#39;                | Baseline | ?        |    90,617.8 ns |     493.07 ns |     411.74 ns |    90,515.3 ns |  1.00 |    0.01 |   3.9063 |  0.6104 |   65.43 KB |        1.00 |
| ParserBenchmarks   | &#39;Typed (200 items)&#39;                | Current  | ?        |    87,271.0 ns |   1,421.32 ns |   1,259.96 ns |    86,853.6 ns |  0.96 |    0.01 |   3.9063 |  0.6104 |   65.43 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| RabbitBenchmarks   | &#39;Publish event&#39;                    | Baseline | ?        |     4,627.4 ns |      69.73 ns |      54.44 ns |     4,622.4 ns |  1.00 |    0.02 |   0.6104 |       - |   10.03 KB |        1.00 |
| RabbitBenchmarks   | &#39;Publish event&#39;                    | Current  | ?        |     4,618.1 ns |      35.42 ns |      31.40 ns |     4,620.2 ns |  1.00 |    0.01 |   0.6104 |       - |   10.03 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| HttpBenchmarks     | &#39;Send command (sender + receiver)&#39; | Baseline | ?        |   192,202.0 ns |   1,595.56 ns |   2,709.39 ns |   191,387.7 ns |  1.00 |    0.02 |   5.8594 |       - |  106.27 KB |        1.00 |
| HttpBenchmarks     | &#39;Send command (sender + receiver)&#39; | Current  | ?        |   191,139.2 ns |   2,406.21 ns |   2,864.43 ns |   189,972.6 ns |  0.99 |    0.02 |   5.8594 |       - |  106.27 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| ParserBenchmarks   | &#39;Embedded $type&#39;                   | Baseline | ?        |       703.6 ns |       8.36 ns |       7.82 ns |       703.5 ns |  1.00 |    0.02 |   0.2069 |  0.0019 |    3.38 KB |        1.00 |
| ParserBenchmarks   | &#39;Embedded $type&#39;                   | Current  | ?        |       709.9 ns |       7.22 ns |       6.75 ns |       708.3 ns |  1.01 |    0.01 |   0.2069 |  0.0019 |    3.38 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| RabbitBenchmarks   | &#39;Consume command (full pipeline)&#39;  | Baseline | ?        |    38,757.4 ns |     195.77 ns |     152.84 ns |    38,719.4 ns |  1.00 |    0.01 |   2.1973 |       - |    37.7 KB |        1.00 |
| RabbitBenchmarks   | &#39;Consume command (full pipeline)&#39;  | Current  | ?        |    39,131.5 ns |     282.17 ns |     235.63 ns |    39,076.6 ns |  1.01 |    0.01 |   2.1973 |       - |    37.7 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| HttpBenchmarks     | &#39;Send query (sender + receiver)&#39;   | Baseline | ?        |   198,833.5 ns |   3,562.63 ns |   6,691.49 ns |   196,662.1 ns |  1.00 |    0.04 |   5.8594 |       - |  106.31 KB |        1.00 |
| HttpBenchmarks     | &#39;Send query (sender + receiver)&#39;   | Current  | ?        |   198,671.0 ns |   3,159.16 ns |   5,933.67 ns |   196,553.9 ns |  1.00 |    0.04 |   5.8594 |       - |  106.17 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| **MediatorBenchmarks** | **Command**                            | **Baseline** | **Bare**     |     **1,920.2 ns** |      **35.86 ns** |      **66.48 ns** |     **1,888.5 ns** |  **1.00** |    **0.05** |   **0.3719** |  **0.0038** |    **6.08 KB** |        **1.00** |
| MediatorBenchmarks | Command                            | Current  | Bare     |     1,870.3 ns |      19.13 ns |      17.90 ns |     1,871.7 ns |  0.98 |    0.03 |   0.3719 |  0.0038 |    6.08 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| MediatorBenchmarks | Query                              | Baseline | Bare     |     1,768.9 ns |      16.77 ns |      15.69 ns |     1,761.2 ns |  1.00 |    0.01 |   0.3757 |  0.0038 |    6.15 KB |        1.00 |
| MediatorBenchmarks | Query                              | Current  | Bare     |     1,759.7 ns |      11.99 ns |      10.01 ns |     1,756.5 ns |  0.99 |    0.01 |   0.3757 |  0.0038 |    6.15 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| MediatorBenchmarks | &#39;Event (2 handlers)&#39;               | Baseline | Bare     |     2,858.2 ns |      16.90 ns |      14.11 ns |     2,857.8 ns |  1.00 |    0.01 |   0.5798 |  0.0076 |     9.5 KB |        1.00 |
| MediatorBenchmarks | &#39;Event (2 handlers)&#39;               | Current  | Bare     |     2,925.1 ns |      25.88 ns |      22.94 ns |     2,918.2 ns |  1.02 |    0.01 |   0.5798 |  0.0076 |     9.5 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| **MediatorBenchmarks** | **Command**                            | **Baseline** | **Full**     |    **27,682.5 ns** |     **246.64 ns** |     **205.95 ns** |    **27,626.8 ns** |  **1.00** |    **0.01** |   **1.7090** |       **-** |   **30.36 KB** |        **1.00** |
| MediatorBenchmarks | Command                            | Current  | Full     |    28,598.2 ns |     569.21 ns |     504.59 ns |    28,445.6 ns |  1.03 |    0.02 |   1.7090 |       - |   30.36 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| MediatorBenchmarks | Query                              | Baseline | Full     |    22,678.3 ns |     228.26 ns |     213.51 ns |    22,652.2 ns |  1.00 |    0.01 |   1.5564 |  0.0305 |   25.85 KB |        1.00 |
| MediatorBenchmarks | Query                              | Current  | Full     |    22,342.3 ns |     252.99 ns |     236.65 ns |    22,299.9 ns |  0.99 |    0.01 |   1.5564 |  0.0305 |   25.85 KB |        1.00 |
|                    |                                    |          |          |                |               |               |                |       |         |          |         |            |             |
| MediatorBenchmarks | &#39;Event (2 handlers)&#39;               | Baseline | Full     |    21,727.3 ns |     249.87 ns |     221.51 ns |    21,669.2 ns |  1.00 |    0.01 |   1.6174 |  0.0305 |   26.54 KB |        1.00 |
| MediatorBenchmarks | &#39;Event (2 handlers)&#39;               | Current  | Full     |    21,825.1 ns |     193.86 ns |     181.34 ns |    21,810.8 ns |  1.00 |    0.01 |   1.6174 |  0.0305 |   26.54 KB |        1.00 |
