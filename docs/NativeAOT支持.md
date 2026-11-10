# NativeAOT 支持

CRules 在 .NET 10 NativeAOT 中通过**显式 Interpreted 后端**执行运行时规则。2026-11-10 已在 Windows x64 和 Linux x64 真实发布、运行当前测试入口，各 21 项必需检查通过；这不是仅运行解释模式单元测试，也不是支持任意 C# 或完整 DLR 的保证。

## 1. 两种后端

| 配置 | 运行方式 | NativeAOT |
| --- | --- | --- |
| JitCompiled（默认） | 将表达式树编译为委托 | 无动态代码能力时拒绝，CRL4004 |
| Interpreted | 标准表达式树解释执行 | 指定能力/类型保留配置下可用 |

```csharp
var compiler = new RuleCompiler(new CompilerOptions {
    ExecutionMode = RuleExecutionMode.Interpreted,
    DecimalOnly = true
});
```

不会静默从 JIT 降级。Executable.ExecutionMode 可用于确认选择，真实产物还应输出/检查 RuntimeFeature.IsDynamicCodeSupported=False。

## 2. 真实发布结果

| 项目 | win-x64 | linux-x64 |
| --- | --- | --- |
| SDK | 10.0.401 | 10.0.302 |
| AOT runtime / ILCompiler | 10.0.14 | 10.0.14 |
| 原生发布 | 成功 | 成功 |
| 动态代码能力 | False | False |
| 必需检查 | 21/21 | 21/21 |
| 运行退出码 | 0 | 0 |
| 最终标记 | AOT_SMOKE_PASS | AOT_SMOKE_PASS |

Windows 使用本机 C++ 工具链；Linux 使用本机已有 dotnet-sdk-aot:10.0 容器，在隔离源码副本中构建，不复用 Windows bin/obj。该容器 SDK 不同，未复制仓库要求 10.0.401 的 global.json。此结果不代表 arm64、其他 libc 或实际服务已通过。

原始日志、警告计数、可执行文件 SHA-256 和体积保存在 [验证记录](data/aot/2026-11-10/verification.json)。源码测试入口为 [AotSmoke](../tests/CRules.AotSmoke/Program.cs)，发布配置见 [项目文件](../tests/CRules.AotSmoke/CRules.AotSmoke.csproj)。

## 3. 已通过的必需能力

- decimal 运算、舍入、布尔、作用域、赋值、只读绑定、if/else/提前 return。
- 编译诊断、禁止默认 JIT 静默退化、除零和溢出异常。
- 命名多参数、显式类型局部声明和运行时 Type 编译/调用入口。
- Dictionary 的 Add/索引初始化、拷贝、索引复合赋值、Add、Remove、TryGetValue。
- DTO 类型自动可见、对象初始化、引用 null 与 nullable 数值 null。
- switch 表达式、List<decimal> 构造和读取。
- Task<T>/ValueTask<T> 等待、nullable 结果与任务异常传播，以及同步结果 ExecuteAsync。
- DataObject.FromJson/RequireDecimal 的有类型 JSON 读取。

列表中的多个断言归并在 21 个命名检查中，不是所有类型排列组合都已实测。普通 JIT/解释回归另有 922 项，不与 AOT 的 42 次平台检查相加宣称语言覆盖率。

## 4. 宿主元数据和原生代码保留

输入/返回类型自动注册、ReferencedTypes 都只解决规则中的类型名称解析。裁剪器不能从任意运行时源码推断需要哪些构造、属性或方法，宿主必须为实际契约保留它们。

下面是一个完整的宿主保留配置示例；All 用于展示保留边界，不代表任意宿主对象图自动保留，也不是成员授权：

<!-- host:aot -->
```csharp
using CRules;
using System.Diagnostics.CodeAnalysis;

return Entry.Run();

public sealed class Context { public decimal Amount { get; init; } }

internal static class Entry {
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Context))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(decimal))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<decimal>))]
    public static int Run() {
        // 明确保留已知闭合值类型集合的反射构造能力。
        GC.KeepAlive(Activator.CreateInstance<List<decimal>>());
        var compiler = new RuleCompiler(new CompilerOptions {
            ExecutionMode = RuleExecutionMode.Interpreted
        });
        var executable = compiler.Compile<Context, List<decimal>>(
            "List<decimal> values = new() { ctx.Amount }; return values;")
            .RequireExecutable();
        return executable.Execute(new Context { Amount = 2m })[0] == 2m ? 0 : 1;
    }
}
```

新增结果 DTO、字段、宿主方法或 Dictionary/List 的闭合类型后，应更新保留配置并重新运行真实 AOT 产物。仅通过普通 dotnet test 或给类型加 ReferencedTypes 不足以完成验收。

## 5. 本轮实际发现的两类问题

**闭合泛型构造缺少代码。** 仅对 List<decimal> 标记 DynamicDependency 仍可能在反射构造时报告没有生成代码。测试宿主加入 `Activator.CreateInstance<List<decimal>>()` 保留已知闭合构造后通过。不能据此承诺运行时随意构造发布前未知的值类型泛型实例。

**异步状态机实例化。** 原解释桥接通过 MakeGenericMethod 创建 AwaitTask<T>，真实产物缺少对应原生代码。当前解释桥接改为等待宿主已有 Task，再读取已保留的 Result；ValueTask<T> 先 AsTask，避免重复消费。核心保留 Task<T>.Result 和 ValueTask<T> 的相关成员。JIT 仍走原有有类型桥接。任务自身的类型和宿主方法仍必须在发布中存在。

## 6. 动态输入、Lambda 和警告

ExpandoObject 的 `(decimal)ctx.Amount` 单字段探测在两平台得到 2；它不计为完整动态能力认证。动态算术、方法、索引、自定义 DynamicObject 和任意形状的成员组合仍需按真实产物逐项验证。可优先选用已通过的 DTO、字典或 DataObject 有类型读取方案。

实验性 Where(...).Sum() 在真实 AOT 中仍返回 CRL3001；严格模式也不允许这类嵌套 Lambda。AllowRuntimeLambdaBinding 不能解决平台缺少运行时代码生成的问题。

发布仍包含 IL2026、IL3050 等裁剪/AOT 分析警告，完整日志已保留，没有全局屏蔽。当前是经过实际发布验证的有限能力范围，不是零警告的库级 AOT 认证。

## 7. 复现

使用满足 global.json 的 .NET SDK 和目标平台 NativeAOT 原生工具链。Windows 需要相应 C++ 工具链与 Windows SDK；Linux 需要 clang、链接器及 zlib 等系统依赖。示例从仓库根目录运行：

```powershell
dotnet publish tests/CRules.AotSmoke -c Release -r win-x64 -o .tmp/aot-check
./.tmp/aot-check/CRules.AotSmoke.exe
```

```sh
# Linux 主机，先确保 SDK/原生工具链与项目要求匹配。
dotnet publish tests/CRules.AotSmoke -c Release -r linux-x64 -o .tmp/aot-check
./.tmp/aot-check/CRules.AotSmoke
```

要求发布成功、运行退出 0、DynamicCodeSupported=False、21 个 PASS、无 FAIL，并以 AOT_SMOKE_PASS 结束。EXPANDO 能力探测和 EXPERIMENTAL_LINQ 输出要单独判断，不能把非门禁探测当作全语法成功。

## 8. 性能含义

NativeAOT 解释执行没有为每条动态规则生成原生机器码。它与 JIT 委托在速度、分配、反射成本上不同，不能套用 [性能报告](性能报告.md) 的 JIT 热调用零分配结论。本轮 profile 只是功能探针的单批样本，不是正式三方 AOT 性能报告。

当前没有发布前生成静态 C# 规则实现的独立后端。如果必须同时获得任意运行时热更新规则和纯静态原生执行成本，当前实现不能承诺两者兼得。
