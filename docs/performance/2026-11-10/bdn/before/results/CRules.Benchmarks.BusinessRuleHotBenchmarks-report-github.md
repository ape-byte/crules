```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-PCPDBF : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=000000000100  IterationCount=5  LaunchCount=3
WarmupCount=3

```
| Method           | Scenario | Mean       | Error       | StdDev      | Median     | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------- |--------- |-----------:|------------:|------------:|-----------:|------:|--------:|----------:|------------:|
| **NativeEquivalent** | **S1**       |   **2.136 ns** |   **0.5233 ns** |   **0.4639 ns** |   **1.964 ns** |  **1.04** |    **0.28** |         **-** |          **NA** |
| CRulesHot        | S1       |   2.700 ns |   1.2234 ns |   1.1444 ns |   2.161 ns |  1.31 |    0.59 |         - |          NA |
|                  |          |            |             |             |            |       |         |           |             |
| **NativeEquivalent** | **S2**       | **261.567 ns** | **315.4969 ns** | **295.1160 ns** | **124.995 ns** |  **1.83** |    **2.30** |         **-** |          **NA** |
| CRulesHot        | S2       | 120.328 ns |  32.9052 ns |  29.1696 ns | 111.585 ns |  0.84 |    0.41 |         - |          NA |
|                  |          |            |             |             |            |       |         |           |             |
| **NativeEquivalent** | **S3**       | **114.309 ns** |   **9.5183 ns** |   **8.9034 ns** | **113.906 ns** |  **1.01** |    **0.11** |         **-** |          **NA** |
| CRulesHot        | S3       | 136.705 ns |  52.0354 ns |  48.6740 ns | 107.472 ns |  1.20 |    0.42 |         - |          NA |
|                  |          |            |             |             |            |       |         |           |             |
| **NativeEquivalent** | **S5**       | **163.561 ns** |  **20.0269 ns** |  **17.7533 ns** | **156.299 ns** |  **1.01** |    **0.14** |         **-** |          **NA** |
| CRulesHot        | S5       | 165.678 ns |  32.3633 ns |  30.2726 ns | 146.350 ns |  1.02 |    0.21 |         - |          NA |
