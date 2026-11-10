```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-DJEDZR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=000000000100  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method               | Mean        | Error        | StdDev      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------- |------------:|-------------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| ClrJsonSnapshot      |  1,507.3 ns |   4,372.2 ns |    239.7 ns |  1.02 |    0.21 | 0.0248 |      - |     160 B |        1.00 |
| JintFreshEngineJson  | 30,057.5 ns | 241,601.8 ns | 13,243.0 ns | 20.31 |    8.38 | 4.4250 | 0.0305 |   27944 B |      174.65 |
| JintCreateAndDispose |    858.0 ns |   2,632.8 ns |    144.3 ns |  0.58 |    0.12 | 1.2608 | 0.0324 |    7920 B |       49.50 |
