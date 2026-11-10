```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-DJEDZR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=000000000100  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method             | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| NativeDirect       |    37.90 ns | 29.01 ns | 1.590 ns |  1.00 |    0.05 |      - |         - |          NA |
| NativeDelegate     |    38.86 ns | 28.76 ns | 1.576 ns |  1.03 |    0.05 |      - |         - |          NA |
| CRulesHot          |    37.50 ns | 18.37 ns | 1.007 ns |  0.99 |    0.04 |      - |         - |          NA |
| JintDecimalHelpers | 1,873.84 ns | 57.22 ns | 3.136 ns | 49.50 |    1.77 | 0.0839 |     544 B |          NA |
