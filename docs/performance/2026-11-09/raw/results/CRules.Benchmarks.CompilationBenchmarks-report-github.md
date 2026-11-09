```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core 5 210H 2.20GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method                   | Mean       | Error       | StdDev     | Gen0    | Gen1   | Allocated |
|------------------------- |-----------:|------------:|-----------:|--------:|-------:|----------:|
| ClrParseAuditCompile     | 536.659 μs | 574.3917 μs | 31.4843 μs | 20.5078 | 4.8828 | 127.79 KB |
| JintPrepareAst           |   5.162 μs |   0.9638 μs |  0.0528 μs |  1.0910 | 0.0305 |   6.73 KB |
| JintPrepareCreateExecute |  12.720 μs |  22.2604 μs |  1.2202 μs |  3.4943 | 0.0153 |  21.48 KB |
