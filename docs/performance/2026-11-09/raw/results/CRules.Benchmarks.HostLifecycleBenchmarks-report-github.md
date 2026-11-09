```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method               | Mean       | Error       | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------- |-----------:|------------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| ClrJsonSnapshot      |   262.6 ns |    52.83 ns |   2.90 ns |  1.00 |    0.01 | 0.0253 |      - |     160 B |        1.00 |
| JintFreshEngineJson  | 8,642.6 ns | 6,350.01 ns | 348.07 ns | 32.92 |    1.19 | 4.4250 | 0.0153 |   27832 B |      173.95 |
| JintCreateAndDispose |   788.5 ns | 1,513.67 ns |  82.97 ns |  3.00 |    0.28 | 1.2617 | 0.0334 |    7920 B |       49.50 |
