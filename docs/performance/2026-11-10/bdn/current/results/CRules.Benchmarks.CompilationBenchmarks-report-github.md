```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-DJEDZR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=000000000100  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method                   | Mean        | Error     | StdDev    | Gen0    | Gen1   | Allocated |
|------------------------- |------------:|----------:|----------:|--------:|-------:|----------:|
| ClrParseAuditCompile     | 1,149.22 μs | 686.23 μs | 37.614 μs | 21.4844 | 7.8125 | 142.93 KB |
| JintPrepareAst           |    15.11 μs |  32.62 μs |  1.788 μs |  1.0986 | 0.0305 |   6.82 KB |
| JintPrepareCreateExecute |    82.18 μs |  82.67 μs |  4.531 μs |  3.4180 | 0.1221 |  21.64 KB |
