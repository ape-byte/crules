# CRules.Abstractions

Public contracts for CRules, targeting **.NET 10**:

- `CompileOptions`, `InputParameter`, `IRuleCompiler` and executable contracts.
- Compilation diagnostics, source spans and execution-mode information.
- The optional immutable JSON `DataObject` with typed reads and explicit missing/null/value states.

This package has no DynamicExpresso dependency. Install **CRules** when you need the compiler and executor; it brings this package transitively.

```csharp
using CRules;

var input = DataObject.FromJson("""{"rate":0.9}""");
decimal rate = input.RequireDecimal("rate");
```

- [Project documentation](https://github.com/ape-byte/crules/blob/github/docs/README.md)
- [Syntax and dynamic data](https://github.com/ape-byte/crules/blob/github/docs/语法参考.md)

Licensed under MIT. This is a preview release; consult the repository for supported behavior and known issues.
