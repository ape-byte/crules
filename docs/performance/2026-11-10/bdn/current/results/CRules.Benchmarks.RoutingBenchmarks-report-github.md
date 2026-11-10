```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-DJEDZR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=000000000100  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method         | Mean        | Error      | StdDev    | Median      | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------- |------------:|-----------:|----------:|------------:|-------:|--------:|-------:|----------:|------------:|
| NativeDirect   |   0.2886 ns |  3.6607 ns | 0.2007 ns |   0.1811 ns |   1.30 |    1.02 |      - |         - |          NA |
| NativeDelegate |   1.3859 ns |  1.0079 ns | 0.0552 ns |   1.3619 ns |   6.25 |    2.72 |      - |         - |          NA |
| CRulesHot      |   0.7269 ns |  2.1259 ns | 0.1165 ns |   0.6840 ns |   3.28 |    1.50 |      - |         - |          NA |
| JintHot        | 220.6636 ns | 43.5400 ns | 2.3866 ns | 220.8754 ns | 994.42 |  430.96 | 0.0076 |      48 B |          NA |
