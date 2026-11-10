```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-DJEDZR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=000000000100  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method             | Mean        | Error        | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------------:|-------------:|-----------:|------:|--------:|-------:|----------:|------------:|
| NativeDirect       |    39.08 ns |    18.717 ns |   1.026 ns |  1.00 |    0.03 |      - |         - |          NA |
| NativeDelegate     |    40.74 ns |     8.000 ns |   0.439 ns |  1.04 |    0.03 |      - |         - |          NA |
| CRulesHot          |    42.67 ns |    23.665 ns |   1.297 ns |  1.09 |    0.04 |      - |         - |          NA |
| JintDecimalHelpers | 1,677.39 ns | 2,721.829 ns | 149.193 ns | 42.94 |    3.45 | 0.0763 |     480 B |          NA |
