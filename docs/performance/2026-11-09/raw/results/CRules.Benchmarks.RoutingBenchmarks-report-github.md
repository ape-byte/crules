```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method         | Mean        | Error       | StdDev    | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------- |------------:|------------:|----------:|-------:|--------:|-------:|----------:|------------:|
| NativeDirect   |   0.4080 ns |   0.5157 ns | 0.0283 ns |   1.00 |    0.09 |      - |         - |          NA |
| NativeDelegate |   1.4120 ns |   1.0826 ns | 0.0593 ns |   3.47 |    0.25 |      - |         - |          NA |
| CRulesHot     |   0.7500 ns |   2.4000 ns | 0.1316 ns |   1.84 |    0.30 |      - |         - |          NA |
| JintHot        | 239.4777 ns | 164.7834 ns | 9.0323 ns | 588.91 |   41.36 | 0.0076 |      48 B |          NA |
