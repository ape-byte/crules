# CRules

**以不变应万变** — one rule configuration model for changing requirements.

CRules is a lightweight wrapper around the public APIs of DynamicExpresso. It accepts a finite C#-style function body, an input signature and a return type, then builds a reusable executable. It is not a fork or a full C# compiler.

This preview targets **.NET 10**.

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

Supported features include named typed inputs, explicit or inferred initialized locals, branches, basic switches, object/List/dictionary initialization, nullable results and host Task/ValueTask results. Compile once and reuse the executable.

**NativeAOT requires `ExecutionMode = RuleExecutionMode.Interpreted` and explicit preservation of host metadata and required closed-generic code.** Windows/Linux x64 tests cover a documented profile, not arbitrary dynamic code. Experimental nested Lambda/LINQ is not part of that AOT profile.

This is a preview: use `Func<...>` or the runtime Type entry point; custom delegate adaptation has a documented known issue. Rules and callable host members must be trusted and reviewed; this is not a complete in-process sandbox.

- [Quick start](https://github.com/ape-byte/crules/blob/github/docs/快速开始.md)
- [Construction and execution](https://github.com/ape-byte/crules/blob/github/docs/构造与执行.md)
- [Syntax 1.0](https://github.com/ape-byte/crules/blob/github/docs/语法参考.md)
- [NativeAOT](https://github.com/ape-byte/crules/blob/github/docs/NativeAOT支持.md)
- [Known issues](https://github.com/ape-byte/crules/blob/github/docs/已知问题.md)

Developed by **ape-byte**. Licensed under MIT. One CRules package contains both the implementation and abstractions assemblies. Its only external package dependency is DynamicExpresso.Core; Jint and Roslyn are not production dependencies.
