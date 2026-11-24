# CRules

**以不变应万变** — one rule configuration model for changing requirements.

CRules is a lightweight wrapper around the public APIs of DynamicExpresso. It accepts a finite C#-style function body, an input signature and a return type, then builds a reusable executable. It is not a fork or a full C# compiler.

This package contains **netstandard2.0, net6.0, net8.0 and net10.0** assets. NuGet selects the nearest compatible target, with net8.0 used on .NET 9.

```csharp
using CRules;

var executable = new RuleCompiler()
    .Compile<Func<decimal, decimal, Dictionary<string, decimal>>>(
        """
        Dictionary<string, decimal> result = new() {
            ["amount"] = amount * rate
        };
        return result;
        """,
        ["amount", "rate"])
    .RequireExecutable();

var result = executable.Execute(100m, 0.9m); // amount = 90m
```

Supported features include named typed inputs, initialized locals, branches, basic switches, object/List/dictionary initialization, nullable results and host Task/ValueTask results. Simple assignments produce a value and support writable locals, dictionary indexes and public set properties of rule-created objects. Compile once and reuse the executable.

**NativeAOT requires `ExecutionMode = RuleExecutionMode.Interpreted` and explicit preservation of host metadata and required closed-generic code.** Windows/Linux x64 tests cover a documented profile, not arbitrary dynamic code. Experimental nested Lambda/LINQ is not part of that AOT profile.

Use `Func<...>` or the runtime Type entry point. See the known issue for custom delegate adaptation. Rules and callable host members must be trusted and reviewed.

- [Quick start](https://github.com/ape-byte/crules/blob/github/docs/快速开始.md)
- [Construction and execution](https://github.com/ape-byte/crules/blob/github/docs/构造与执行.md)
- [Syntax 1.0](https://github.com/ape-byte/crules/blob/github/docs/语法参考.md)
- [NativeAOT](https://github.com/ape-byte/crules/blob/github/docs/NativeAOT支持.md)
- [Known issues](https://github.com/ape-byte/crules/blob/github/docs/已知问题.md)

Developed by **ape-byte**. Licensed under MIT. One CRules package contains both the implementation and abstractions assemblies. Its external package dependency is DynamicExpresso.Core, plus System.Text.Json 8.0.6 for netstandard2.0/net6.0; Jint and Roslyn are not production dependencies.
