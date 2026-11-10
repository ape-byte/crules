```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-DJEDZR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=000000000100  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method                   | Mean      | Error     | StdDev    | Gen0    | Gen1   | Allocated |
|------------------------- |----------:|----------:|----------:|--------:|-------:|----------:|
| ClrParseAuditCompile     | 734.39 μs | 764.53 μs | 41.906 μs | 21.4844 | 4.8828 | 132.97 KB |
| JintPrepareAst           |  15.64 μs |  10.52 μs |  0.577 μs |  1.0986 | 0.0305 |   6.82 KB |
| JintPrepareCreateExecute |  41.25 μs |  50.20 μs |  2.752 μs |  3.4790 | 0.0610 |  21.64 KB |
