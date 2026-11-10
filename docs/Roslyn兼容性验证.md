# CRules Syntax 1.0：Roslyn 官方用例与差分验证

CRules 的支持范围以 [Syntax 1.0](CRules-Syntax-1.0.md) 为准。本轮从 Roslyn 官方库寻找对应语法测试，并把能够独立执行的规则核心纳入持续回归。每个纳入的正向片段必须在两种执行模式下与 Roslyn 一致；已识别的错误语义必须拒绝，不能通过静默转换得到结果。

## 固定来源

官方仓库为 [dotnet/roslyn](https://github.com/dotnet/roslyn)，本轮固定提交 `636a46f73c2006fad15339a5ba8c9ac5cd1c10c8`，避免未来 main 分支变化影响结论。

| 语法与行为 | 官方测试来源 |
| --- | --- |
| 常量 switch、分组、default、break、nullable、字符串与整数边界 | [Emit/CodeGen/SwitchTests.cs](https://github.com/dotnet/roslyn/blob/636a46f73c2006fad15339a5ba8c9ac5cd1c10c8/src/Compilers/CSharp/Test/Emit/CodeGen/SwitchTests.cs) |
| 标签转换、重复标签、贯穿错误、闭合 bool? 返回路径 | [Semantic/Semantics/SwitchTests.cs](https://github.com/dotnet/roslyn/blob/636a46f73c2006fad15339a5ba8c9ac5cd1c10c8/src/Compilers/CSharp/Test/Semantic/Semantics/SwitchTests.cs) |
| switch 表达式与结果类型 | [PatternMatchingTests2.cs](https://github.com/dotnet/roslyn/blob/636a46f73c2006fad15339a5ba8c9ac5cd1c10c8/src/Compilers/CSharp/Test/Emit3/Semantics/PatternMatchingTests2.cs)、[PatternMatchingTests3.cs](https://github.com/dotnet/roslyn/blob/636a46f73c2006fad15339a5ba8c9ac5cd1c10c8/src/Compilers/CSharp/Test/Emit3/Semantics/PatternMatchingTests3.cs)、[TargetTypedSwitchExpressionTests.cs](https://github.com/dotnet/roslyn/blob/636a46f73c2006fad15339a5ba8c9ac5cd1c10c8/src/Compilers/CSharp/Test/Semantic/Semantics/TargetTypedSwitchExpressionTests.cs) |
| 局部复合赋值、空值合并、算术与返回转换 | [CodeGenOperators.cs](https://github.com/dotnet/roslyn/blob/636a46f73c2006fad15339a5ba8c9ac5cd1c10c8/src/Compilers/CSharp/Test/Emit/CodeGen/CodeGenOperators.cs)、[CodeGenTests.cs](https://github.com/dotnet/roslyn/blob/636a46f73c2006fad15339a5ba8c9ac5cd1c10c8/src/Compilers/CSharp/Test/Emit/CodeGen/CodeGenTests.cs) |
| 数组、元素推断、List 与对象初始化 | [CodeGenImplicitlyTypeArraysTests.cs](https://github.com/dotnet/roslyn/blob/636a46f73c2006fad15339a5ba8c9ac5cd1c10c8/src/Compilers/CSharp/Test/Emit/CodeGen/CodeGenImplicitlyTypeArraysTests.cs)、[ObjectAndCollectionInitializerTests.cs](https://github.com/dotnet/roslyn/blob/636a46f73c2006fad15339a5ba8c9ac5cd1c10c8/src/Compilers/CSharp/Test/Emit/CodeGen/ObjectAndCollectionInitializerTests.cs)、[ImplicitObjectCreationTests.cs](https://github.com/dotnet/roslyn/blob/636a46f73c2006fad15339a5ba8c9ac5cd1c10c8/src/Compilers/CSharp/Test/Semantic/Semantics/ImplicitObjectCreationTests.cs) |
| if/else、返回路径与非法 break | [FlowTests.cs](https://github.com/dotnet/roslyn/blob/636a46f73c2006fad15339a5ba8c9ac5cd1c10c8/src/Compilers/CSharp/Test/Emit3/FlowAnalysis/FlowTests.cs)、[FlowDiagnosticTests.cs](https://github.com/dotnet/roslyn/blob/636a46f73c2006fad15339a5ba8c9ac5cd1c10c8/src/Compilers/CSharp/Test/Emit3/FlowAnalysis/FlowDiagnosticTests.cs) |

完整路径与源码哈希在 [upstream.json](../tests/CRules.Tests/Roslyn/upstream.json)，原版权和 MIT 许可保存在 [测试来源说明](../tests/CRules.Tests/Roslyn/NOTICE.md)。

## 纳入标准与测试方法

盘点上述 12 个文件中的 1,719 个 Fact/Theory 方法，自动提取签名和语句处于支持范围、能够脱离原夹具独立编译的 267 个函数核心或集合创建表达式。另补 18 个手工移植/边界变体、14 个错误语义片段。片段逐条关联原测试名与改写说明。

正向片段由测试依赖 Microsoft.CodeAnalysis.CSharp 5.0.0 编译为参照程序集，语言版本固定 C# 9。CRules 的 JIT 和 Interpreted 接收相同代码与签名，遍历输入组合及 null、负值、零、普通值、类型边界，比较返回内容或异常类型。集合递归比较元素；对象初始化比较公开数据。规则函数中的调用和异常均发生在执行阶段。

14 个负向片段先确认 Roslyn 报错，再确认 CRules 返回编译诊断，覆盖重复标签/default、运行时 case、null/数值标签转换、非法贯穿、缺少返回、无目标 break、非法复合赋值与重复成员初始化。

原测试中的完整 Main、Console 输出、IL 断言和 Roslyn 专用夹具没有直接搬入引擎。显式局部声明改为 var 加同类型转换；可写形参复制到局部；自增减语句改写为复合赋值；删除不可达尾句。直接数组/List 创建表达式则保留元素、类型与顺序，作为返回值比较。

这是一份来源可追溯的有限语法兼容回归，不是整个 Roslyn 测试仓库的通过证明。语言版本开关、语法树/诊断/IL 的精确输出、泛型夹具、循环、异常、模式、源生成等未纳入执行器契约；无法直接提取的项目在 [inventory.json](../tests/CRules.Tests/Roslyn/inventory.json) 中保留原因，不把它们计算为测试通过或测试跳过。

## 本轮修复

- 分开跟踪继续执行、return 与 break，修复条件 break 后仍被误判为全部返回的问题。
- 常量 switch 选择值、简单布尔常量条件及 bool/bool? 闭合值域参与返回路径证明。
- 标签转换拒绝超范围或无隐式转换的值；错误转成诊断，不泄漏 OverflowException/ArgumentException。
- 复合赋值保留合法的小整数回转转换，拒绝任意截断右值；形参与只读局部不能被修改。
- 补充一维/交错数组、隐式元素推断、长度校验、null 元素和 List 初始化适配。
- 补充默认结构体构造、科学计数法 float 字面量及命名 char/char? 空值合并的上游兼容缺口。
- 拒绝源码中的 ++/--，避免上游把前置 ++ 误当作重复一元加号。字符串中的相同文本保持原值。

所有适配仍通过 DynamicExpresso 公开 API 和标准表达式树完成；核心包未引用 Roslyn，也未 fork DynamicExpresso。Roslyn 仅用于测试和离线用例提取工具。

## 复现

2026-11-10 验收：完整回归 **720 项通过、0 失败、0 跳过**，其中兼容回归 584 项（285 个正向片段 × 两种模式 + 14 个负向片段）；Release 构建 0 警告、0 错误。已从固定版本重新下载并提取官方来源，生成的用例、盘点与来源清单一致；6 个文档示例的输出断言通过。具体版本与验收范围见 [验证记录](validation/2026-11-10/syntax-1.0/verification.json)。

```powershell
dotnet restore CRules.slnx --locked-mode
dotnet build CRules.slnx -c Release --no-restore
dotnet test tests/CRules.Tests -c Release --no-build --no-restore
```

仅执行兼容回归可追加 `--filter FullyQualifiedName~RoslynCompatibilityTests`。日常测试使用仓库内的固定片段，无需下载官方源码。更新来源的步骤见测试目录说明。

本轮没有重新发布 NativeAOT，也没有重测性能。历史 AOT/性能证据不自动覆盖新的签名、动态绑定、异步或集合构造适配。
