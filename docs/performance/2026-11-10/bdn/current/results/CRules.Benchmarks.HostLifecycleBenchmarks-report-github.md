```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-DJEDZR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=000000000100  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method               | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| ClrJsonSnapshot      |  2.042 μs |  2.818 μs | 0.1545 μs |  1.00 |    0.09 | 0.0229 |      - |     160 B |        1.00 |
| JintFreshEngineJson  | 27.136 μs | 35.408 μs | 1.9409 μs | 13.34 |    1.18 | 4.4556 | 0.0610 |   28136 B |      175.85 |
| JintCreateAndDispose |  1.222 μs |  6.285 μs | 0.3445 μs |  0.60 |    0.15 | 1.2589 | 0.0305 |    7920 B |       49.50 |
