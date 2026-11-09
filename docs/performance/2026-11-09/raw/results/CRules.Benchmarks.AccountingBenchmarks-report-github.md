```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method             | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| NativeDirect       |    39.33 ns |  13.73 ns |  0.752 ns |  1.00 |    0.02 |      - |         - |          NA |
| NativeDelegate     |    40.84 ns |  27.43 ns |  1.503 ns |  1.04 |    0.04 |      - |         - |          NA |
| CRulesHot         |    38.73 ns |  33.43 ns |  1.832 ns |  0.98 |    0.04 |      - |         - |          NA |
| JintDecimalHelpers | 1,458.31 ns | 690.36 ns | 37.841 ns | 37.08 |    1.03 | 0.0763 |     480 B |          NA |
