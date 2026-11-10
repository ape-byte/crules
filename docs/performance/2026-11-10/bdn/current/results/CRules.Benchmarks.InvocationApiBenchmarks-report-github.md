```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-DJEDZR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=000000000100  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method                 | Mean       | Error      | StdDev    | Median     | Gen0   | Allocated |
|----------------------- |-----------:|-----------:|----------:|-----------:|-------:|----------:|
| TypedContext           |  0.2741 ns |  6.0536 ns | 0.3318 ns |  0.1793 ns |      - |         - |
| RuntimeTypeContext     | 22.6177 ns | 35.9615 ns | 1.9712 ns | 22.3305 ns | 0.0088 |      56 B |
| TypedNamedInputs       | 14.5478 ns | 22.1210 ns | 1.2125 ns | 14.3310 ns |      - |         - |
| RuntimeTypeNamedInputs | 55.7644 ns | 67.6720 ns | 3.7093 ns | 56.6139 ns | 0.0215 |     136 B |
