```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
AMD Ryzen 7 5700X 3.40GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.203
  [Host]   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Baseline : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Current  : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Type               | Method                             | Job      | Pipeline | Mean           | Error        | StdDev        | Median         | Ratio | RatioSD | Gen0     | Gen1   | Allocated  | Alloc Ratio |
|------------------- |----------------------------------- |--------- |--------- |---------------:|-------------:|--------------:|---------------:|------:|--------:|---------:|-------:|-----------:|------------:|
| **HttpBenchmarks**     | **&#39;Receive command&#39;**                  | **Baseline** | **?**        |   **155,074.8 ns** |  **2,606.53 ns** |   **5,265.32 ns** |   **154,009.1 ns** |  **1.00** |    **0.04** |   **5.8594** |      **-** |   **96.34 KB** |        **1.00** |
| HttpBenchmarks     | &#39;Receive command&#39;                  | Current  | ?        |   121,883.1 ns |  1,546.40 ns |   1,291.32 ns |   121,686.4 ns |  0.79 |    0.02 |   5.3711 |      - |   90.87 KB |        0.94 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| ParserBenchmarks   | &#39;Typed (small)&#39;                    | Baseline | ?        |     1,481.2 ns |     15.24 ns |      13.51 ns |     1,475.6 ns |  1.00 |    0.01 |   0.2079 | 0.0019 |    3.41 KB |        1.00 |
| ParserBenchmarks   | &#39;Typed (small)&#39;                    | Current  | ?        |       507.9 ns |      5.21 ns |       4.62 ns |       505.7 ns |  0.34 |    0.00 |   0.1841 | 0.0010 |    3.01 KB |        0.88 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| RabbitBenchmarks   | &#39;Send command&#39;                     | Baseline | ?        |     4,862.3 ns |     56.02 ns |      49.66 ns |     4,844.5 ns |  1.00 |    0.01 |   0.6104 |      - |   10.18 KB |        1.00 |
| RabbitBenchmarks   | &#39;Send command&#39;                     | Current  | ?        |     4,826.1 ns |     46.35 ns |      41.09 ns |     4,829.0 ns |  0.99 |    0.01 |   0.6104 |      - |   10.18 KB |        1.00 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| StartupBenchmarks  | &#39;Build and start UseDefault host&#39;  | Baseline | ?        | 5,644,769.2 ns | 73,893.26 ns | 129,418.10 ns | 5,602,775.0 ns |  1.00 |    0.03 | 187.5000 |      - |  3127.3 KB |        1.00 |
| StartupBenchmarks  | &#39;Build and start UseDefault host&#39;  | Current  | ?        | 5,305,453.6 ns | 41,345.51 ns |  55,195.08 ns | 5,283,662.5 ns |  0.94 |    0.02 | 171.8750 |      - | 3055.56 KB |        0.98 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| HttpBenchmarks     | &#39;Receive query&#39;                    | Baseline | ?        |   164,587.9 ns |  2,109.60 ns |   3,910.27 ns |   163,490.8 ns |  1.00 |    0.03 |   5.8594 |      - |   95.67 KB |        1.00 |
| HttpBenchmarks     | &#39;Receive query&#39;                    | Current  | ?        |   126,102.6 ns |  1,261.70 ns |   1,118.46 ns |   126,162.3 ns |  0.77 |    0.02 |   5.3711 |      - |   87.89 KB |        0.92 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| ParserBenchmarks   | &#39;Typed (200 items)&#39;                | Baseline | ?        |    89,518.9 ns |    533.37 ns |     416.42 ns |    89,551.3 ns |  1.00 |    0.01 |   3.9063 | 0.6104 |   65.43 KB |        1.00 |
| ParserBenchmarks   | &#39;Typed (200 items)&#39;                | Current  | ?        |    87,196.1 ns |    691.67 ns |     577.58 ns |    87,010.5 ns |  0.97 |    0.01 |   3.9063 | 0.6104 |   65.02 KB |        0.99 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| RabbitBenchmarks   | &#39;Publish event&#39;                    | Baseline | ?        |     4,534.7 ns |     47.70 ns |      39.83 ns |     4,531.6 ns |  1.00 |    0.01 |   0.6104 |      - |   10.03 KB |        1.00 |
| RabbitBenchmarks   | &#39;Publish event&#39;                    | Current  | ?        |     4,696.2 ns |     43.87 ns |      36.63 ns |     4,693.5 ns |  1.04 |    0.01 |   0.6104 |      - |   10.04 KB |        1.00 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| HttpBenchmarks     | &#39;Send command (sender + receiver)&#39; | Baseline | ?        |   188,980.6 ns |  3,614.11 ns |   7,217.77 ns |   187,256.5 ns |  1.00 |    0.05 |   5.8594 |      - |  106.27 KB |        1.00 |
| HttpBenchmarks     | &#39;Send command (sender + receiver)&#39; | Current  | ?        |   160,733.9 ns |  2,830.73 ns |   4,957.80 ns |   159,375.2 ns |  0.85 |    0.04 |   5.8594 |      - |  100.68 KB |        0.95 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| ParserBenchmarks   | &#39;Embedded $type&#39;                   | Baseline | ?        |       728.0 ns |      7.19 ns |       6.38 ns |       727.1 ns |  1.00 |    0.01 |   0.2069 | 0.0019 |    3.38 KB |        1.00 |
| ParserBenchmarks   | &#39;Embedded $type&#39;                   | Current  | ?        |       740.7 ns |      7.68 ns |       6.81 ns |       739.2 ns |  1.02 |    0.01 |   0.2069 | 0.0019 |    3.38 KB |        1.00 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| RabbitBenchmarks   | &#39;Consume command (full pipeline)&#39;  | Baseline | ?        |    39,399.8 ns |    380.49 ns |     317.73 ns |    39,480.3 ns |  1.00 |    0.01 |   2.1973 |      - |    37.7 KB |        1.00 |
| RabbitBenchmarks   | &#39;Consume command (full pipeline)&#39;  | Current  | ?        |    17,255.4 ns |    264.44 ns |     234.42 ns |    17,186.6 ns |  0.44 |    0.01 |   1.9531 |      - |   32.13 KB |        0.85 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| HttpBenchmarks     | &#39;Send query (sender + receiver)&#39;   | Baseline | ?        |   199,405.3 ns |  3,538.76 ns |   5,912.47 ns |   196,563.5 ns |  1.00 |    0.04 |   5.8594 |      - |  106.17 KB |        1.00 |
| HttpBenchmarks     | &#39;Send query (sender + receiver)&#39;   | Current  | ?        |   165,934.4 ns |  3,291.49 ns |   5,124.46 ns |   164,168.8 ns |  0.83 |    0.03 |   5.8594 |      - |  100.47 KB |        0.95 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| **MediatorBenchmarks** | **Command**                            | **Baseline** | **Bare**     |     **1,912.7 ns** |     **19.74 ns** |      **17.50 ns** |     **1,906.6 ns** |  **1.00** |    **0.01** |   **0.3719** | **0.0038** |    **6.08 KB** |        **1.00** |
| MediatorBenchmarks | Command                            | Current  | Bare     |     1,626.8 ns |     31.46 ns |      49.90 ns |     1,605.8 ns |  0.85 |    0.03 |   0.3643 | 0.0038 |    5.97 KB |        0.98 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| MediatorBenchmarks | Query                              | Baseline | Bare     |     1,817.1 ns |     15.92 ns |      14.11 ns |     1,817.3 ns |  1.00 |    0.01 |   0.3757 | 0.0038 |    6.15 KB |        1.00 |
| MediatorBenchmarks | Query                              | Current  | Bare     |     1,540.5 ns |     14.41 ns |      13.48 ns |     1,540.0 ns |  0.85 |    0.01 |   0.3624 | 0.0038 |    5.95 KB |        0.97 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| MediatorBenchmarks | &#39;Event (2 handlers)&#39;               | Baseline | Bare     |     2,925.2 ns |     36.25 ns |      33.90 ns |     2,914.5 ns |  1.00 |    0.02 |   0.5798 | 0.0076 |     9.5 KB |        1.00 |
| MediatorBenchmarks | &#39;Event (2 handlers)&#39;               | Current  | Bare     |     2,583.9 ns |     25.30 ns |      21.12 ns |     2,585.1 ns |  0.88 |    0.01 |   0.6256 | 0.0114 |   10.26 KB |        1.08 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| **MediatorBenchmarks** | **Command**                            | **Baseline** | **Full**     |    **27,700.2 ns** |    **295.46 ns** |     **261.92 ns** |    **27,568.9 ns** |  **1.00** |    **0.01** |   **1.7090** |      **-** |   **30.47 KB** |        **1.00** |
| MediatorBenchmarks | Command                            | Current  | Full     |    13,091.3 ns |    195.03 ns |     172.88 ns |    13,040.9 ns |  0.47 |    0.01 |   1.4648 |      - |   25.63 KB |        0.84 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| MediatorBenchmarks | Query                              | Baseline | Full     |    22,951.1 ns |    383.15 ns |     376.30 ns |    22,851.4 ns |  1.00 |    0.02 |   1.5564 | 0.0305 |   25.85 KB |        1.00 |
| MediatorBenchmarks | Query                              | Current  | Full     |     9,722.9 ns |     63.79 ns |      56.55 ns |     9,725.0 ns |  0.42 |    0.01 |   1.2817 | 0.0153 |   20.95 KB |        0.81 |
|                    |                                    |          |          |                |              |               |                |       |         |          |        |            |             |
| MediatorBenchmarks | &#39;Event (2 handlers)&#39;               | Baseline | Full     |    22,128.4 ns |    177.76 ns |     157.58 ns |    22,124.1 ns |  1.00 |    0.01 |   1.6174 | 0.0305 |   26.54 KB |        1.00 |
| MediatorBenchmarks | &#39;Event (2 handlers)&#39;               | Current  | Full     |     9,851.7 ns |     94.89 ns |      84.12 ns |     9,833.9 ns |  0.45 |    0.00 |   1.3733 | 0.0305 |   22.65 KB |        0.85 |
