# NativeAOT 支持与限制

CRules 增加显式 `Interpreted` 后端，用于 .NET 10 NativeAOT 下的受限运行时规则求值。默认 `JitCompiled` 后端在 `RuntimeFeature.IsDynamicCodeSupported=false` 时返回 CRL4004，不会静默切换执行方式。规则句柄的 `ExecutionMode` 可供宿主审计。

## 已验证能力

本机使用 Windows x64、.NET SDK 10.0.401、Visual Studio 2026 C++ 工具链和 Windows SDK 实际发布 NativeAOT 可执行文件。AOT runtime 和 ILCompiler 固定为 10.0.14，运行时输出 `DynamicCodeSupported=False`。验证工程为 `tests/CRules.AotSmoke`，运行结果与发布分析日志存于 `docs/aot/2026-11-09`，13 项必需检查全部通过。

AOT 标量范围包括 decimal 运算、var/const、局部变量赋值、嵌套块、if/else、提前 return、布尔返回、舍入、编译诊断、禁止隐式退化，以及除零和溢出失败。宿主需保存上下文和所允许类型的反射元数据。动态 Metadata 不在支持范围内。

## 接入方式

```csharp
var compiler = new RuleCompiler(new RuleCompileOptions
{
    DecimalOnly = true,
    ExecutionMode = RuleExecutionMode.Interpreted
});
var rule = compiler.Compile<MyContext, decimal>(source).RequireRule();
decimal amount = rule.Execute(context);
```

在 AOT 宿主入口用 `DynamicDependency` 或 linker descriptor 保留 `MyContext`、嵌套 DTO 及批准的方法和运算符元数据。示例保留 AotInput、decimal 和 Math；这不是针对任意宿主对象图的自动保留方案，也不是成员安全许可。新增类型需在真实 AOT 发布产物上验证。

```csharp
[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MyContext))]
[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(decimal))]
[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Math))]
static int RunAotRules() { /* 编译及执行 */ }
```

```powershell
dotnet publish tests/CRules.AotSmoke -c Release -r win-x64 -o .tmp/aot
./.tmp/aot/CRules.AotSmoke.exe
```

预期退出码 0，并输出 `AOT_SMOKE_PASS`。默认模式 guard 与显式解释模式必须分别测试，不能仅靠 publish 成功判断规则可执行。

## 性能与能力边界

NativeAOT 不支持运行时生成规则机器码。标准表达式树在此环境使用解释执行；这保持强类型委托入口和数值语义，但不满足 JIT 路径的热执行零分配与完全无反射目标。真正的 AOT 原生热路径需要在发布前固定规则并生成或注册静态 C# 实现，这属于后续构建期规则后端，当前未实现。

在实际 NativeAOT 产物中，实验性 `Where(...).Sum()` 规则在上游解析阶段报告 CRL3001；严格模式也不会执行这类规则。本次不声明任意 LINQ、Lambda、泛型方法或动态 schema 在 AOT 下可用。若业务必须在 AOT 中热更新任意规则，应接受解释模式的能力和成本边界；不能承诺同时获得运行时动态规则与静态原生性能。

上游 DynamicExpresso 和依赖的动态绑定代码仍会产生 IL2026、IL3050 等裁剪及 AOT 分析警告，日志原样保留，未全局屏蔽。当前是经过实际发布与运行验证的标量兼容 profile，不是库级零警告或完整 AOT 兼容认证。

## 官方依据

[Microsoft NativeAOT 部署说明](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) 明确限制运行时代码生成，表达式树采用解释形式。

[DynamicExpresso AOT 支持讨论](https://github.com/dynamicexpresso/DynamicExpresso/issues/283) 记录了上游裁剪与 AOT 问题；本项目通过实际发布测试界定支持子集，未据此修改上游源码。
