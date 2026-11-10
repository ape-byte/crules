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
| **NativeEquivalent** | **S1**       |   **1.793 ns** |   **0.5295 ns** |   **0.4694 ns** |   **1.591 ns** |  **1.06** |    **0.36** |         **-** |          **NA** |
| CRulesHot        | S1       |   1.567 ns |   0.2783 ns |   0.2603 ns |   1.485 ns |  0.92 |    0.25 |         - |          NA |
|                  |          |            |             |             |            |       |         |           |             |
| **NativeEquivalent** | **S2**       |  **96.196 ns** |   **3.8441 ns** |   **3.4077 ns** |  **96.850 ns** |  **1.00** |    **0.05** |         **-** |          **NA** |
| CRulesHot        | S2       | 102.484 ns |  12.6939 ns |  11.2528 ns |  98.770 ns |  1.07 |    0.12 |         - |          NA |
|                  |          |            |             |             |            |       |         |           |             |
| **NativeEquivalent** | **S3**       | **110.481 ns** |  **14.7573 ns** |  **13.0819 ns** | **106.434 ns** |  **1.01** |    **0.16** |         **-** |          **NA** |
| CRulesHot        | S3       | 104.051 ns |   5.8408 ns |   5.1777 ns | 102.113 ns |  0.95 |    0.11 |         - |          NA |
|                  |          |            |             |             |            |       |         |           |             |
| **NativeEquivalent** | **S5**       | **403.445 ns** | **578.8100 ns** | **513.1000 ns** | **148.740 ns** |  **2.25** |    **3.21** |         **-** |          **NA** |
| CRulesHot        | S5       | 174.698 ns |  32.1838 ns |  30.1048 ns | 162.866 ns |  0.97 |    0.48 |         - |          NA |
