```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-DJEDZR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=000000000100  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method         | Mean        | Error         | StdDev     | Ratio    | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------- |------------:|--------------:|-----------:|---------:|--------:|-------:|----------:|------------:|
| NativeDirect   |   0.4117 ns |     1.3179 ns |  0.0722 ns |     1.02 |    0.23 |      - |         - |          NA |
| NativeDelegate |   1.0954 ns |     1.8488 ns |  0.1013 ns |     2.72 |    0.50 |      - |         - |          NA |
| CRulesHot      |   0.5204 ns |     3.9596 ns |  0.2170 ns |     1.29 |    0.52 |      - |         - |          NA |
| JintHot        | 528.3827 ns | 1,010.5254 ns | 55.3903 ns | 1,312.33 |  246.43 | 0.0076 |      48 B |          NA |
