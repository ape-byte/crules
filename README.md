<p align="center">
  <img src="assets/branding/crules-icon.png" width="144" alt="CRules 灰橙图标">
</p>

<h1 align="center">CRules</h1>

<p align="center"><strong>以不变应万变</strong></p>
<p align="center">以一种规则配置方式，应对万变的规则配置需求。</p>
<p align="center">动态规则 · 强类型 · decimal 精度 · 编译后复用</p>

<p align="center">
  <a href="#快速开始">快速开始</a> ·
  <a href="#与-jint-的简要对比">Jint 对比</a> ·
  <a href="#让-ai-生成规则并验证结果">AI 与规则验证</a> ·
  <a href="docs/README.md">完整文档</a>
</p>

**CRules 是面向动态规则配置的轻量 .NET 规则编译器。** 把经常变化的条件、计算和决策写成规则代码，加载时编译成强类型委托，运行时传入上下文并重复执行。

名称中的 **C** 可以理解为 **CsharpRules**（C# 风格的规则）、**ClrRules**（运行于 CLR 的规则），也可以是 **ContextRules**（由上下文驱动的规则）。

CRules 基于 [DynamicExpresso](https://github.com/dynamicexpresso/DynamicExpresso) 的**公开 API 轻量封装，没有 fork 上游源码**。表达式解析与类型检查交给成熟的动态表达式库，CRules 补充有限语句、分支、局部变量、返回值和执行契约。

## 让规则变化成为配置变化

折扣条件、计费公式、路由策略、资格判断经常改变。CRules 为这些变化提供统一入口：**定义上下文 → 编写规则 → 验证输入输出 → 加载执行**。

在既有上下文字段和规则能力范围内，调整逻辑只需更新规则配置，无需为了每次需求变化重新构建和发布应用。新增数据来源、上下文字段或宿主能力时，仍需更新相应应用代码。

| 能力 | 作用 |
| --- | --- |
| 动态规则配置 | 用同一种代码式规则承载不同条件、公式和决策，降低专用配置结构的维护成本 |
| 强类型编译 | 准备阶段检查字段、类型和返回值，支持局部变量、`if/else`、提前返回及对象结果 |
| 编译后重复执行 | JIT 模式将整份规则编译为一个 `Func<TContext, TResult>`，热执行不重复解析源码 |
| decimal 数值策略 | 十进制运算可直接使用 `decimal`，支持编译期拒绝 `float/double` 运算 |
| 可扩展上下文 | 固定字段使用公开 C# 类型，动态字段使用 `RuleMetadata` 有类型读取 |
| 适合 AI 生成 | 规则、输入和预期输出均有明确契约，方便 AI 生成、修改并交给同一引擎验证 |

## 让 AI 生成规则并验证结果

AI 时代，需求可以直接转成规则配置。AI 根据业务描述和上下文契约编写规则，再构造多组 mock 输入与预期结果，使用 CRules 编译和执行，检查正常、分支、边界及缺失数据场景。

```text
需求与上下文契约 → AI 生成规则与 mock 用例 → 编译和执行验证 → 宿主加载已验证规则
```

这让应用可以在稳定的执行接口上持续适应新需求。CRules 提供规则编译与执行能力；AI 调用、用例组织和配置发布由宿主应用接入。预期结果应依据业务约定确定，避免仅用待验证规则本身反算答案。

## 与 Jint 的简要对比

**在已测的规则复用场景中，CRules 比 Jint 执行更快。** 下表来自 2026-11-09 的 .NET 10 JIT 测试：规则或函数准备一次，每组运行 100,000 次，计时内逐次创建随机上下文并校验输出，取三个独立进程结果的中位数。Jint 列选取各场景三种复用 Engine 入口中较快者。

| 场景 | CRules | Jint 较快复用入口 | 耗时比 Jint / CRules |
| --- | ---: | ---: | ---: |
| 小上下文、简单规则 | 106.88 ms | 754.10 ms | **7.06×** |
| 小上下文、100 个变量与 32 个条件节点 | 82.13 ms | 5,060.52 ms | **61.62×** |
| 大上下文、简单规则 | 1,226.71 ms | 1,858.94 ms | **1.52×** |
| 大上下文、100 个变量与 32 个条件节点 | 1,208.23 ms | 15,140.57 ms | **12.53×** |

输入转换和规则复杂度会改变收益，表中倍数只适用于已测工作负载。该基准使用安全整数／定点语义，没有给 Jint 额外叠加 decimal 转换成本。CRules 的准备成本较高；在复杂场景每次重新准备的 200 次测试中约为 20,020 ms，Jint 单例逐次解析约为 532 ms，适合在加载或变更时编译，再反复调用。

| 关注点 | CRules | Jint |
| --- | --- | --- |
| 规则执行方式 | 有限 C# 风格代码段，JIT 下编译为强类型委托 | JavaScript 运行时 |
| 常见十进制计算 | 原生 `decimal` 运算，支持 `DecimalOnly` 约束 | 默认 `Number` 使用二进制浮点；十进制精确计算需额外设计 |
| 更适合的需求 | 频繁调整、重复执行的条件与计算规则 | 需要 JavaScript 语言与运行时语义的脚本 |

完整环境、内存、首次准备与复现步骤见 [性能对比报告](docs/性能测试对比.md)，可视化见 [离线交互报告](docs/performance/2026-11-09/business-100k/index.html)。本次 README 更新沿用已有测量，没有重新跑基准。

## 十进制精度直接表达

价格、金额、比例可以直接使用 `decimal`，避免 JavaScript `Number` 常见的二进制浮点误差，例如 `0.1m + 0.2m == 0.3m`。启用 `DecimalOnly = true` 后，编译器拒绝 `float/double` 子树和未转换为 decimal 的整数算术，减少精度策略被无意破坏的机会。

规则显式指定舍入位数和模式；除零与溢出明确失败。decimal 具有有限范围和精度，需要舍入的除法仍应按业务约定处理。

## 快速开始

需要 .NET 10；仓库通过 `global.json` 固定 SDK **10.0.401**，允许同特性带的更新补丁。

```powershell
# 构建独立核心
dotnet restore src/CRules/CRules.csproj --locked-mode
dotnet build src/CRules/CRules.csproj -c Release --no-restore

# 在你的应用中引用
dotnet add <应用项目.csproj> reference <CRules目录>/src/CRules/CRules.csproj
```

宿主定义上下文，在规则加载时编译，在每次请求中传入数据。下面同时演示规则执行和多场景 mock 验证：

```csharp
using CRules;

var compiler = new RuleCompiler(new RuleCompileOptions { DecimalOnly = true });

var compilation = compiler.Compile<PriceContext, decimal>(new RuleSource(
    RuleId: "discount",
    Revision: "1",
    Text: """
        var multiplier = ctx.Metadata.GetDecimal("multiplier") ?? 1m;
        var amount = ctx.Amount * multiplier;

        if (ctx.IsVip) {
            return Math.Round(amount * 0.9m, 2, MidpointRounding.ToEven);
        } else {
            return Math.Round(amount, 2, MidpointRounding.ToEven);
        }
        """));

var rule = compilation.RequireRule();
var context = new PriceContext(100m, true,
    RuleMetadata.FromJson("""{"multiplier":1.2}"""));

Console.WriteLine(rule.Execute(context)); // 108

// AI 或开发者可以按业务约定生成这些输入与期望值。
var cases = new[]
{
    (Input: context, Expected: 108m),
    (Input: new PriceContext(100m, true, RuleMetadata.Empty), Expected: 90m),
    (Input: new PriceContext(100m, false, RuleMetadata.Empty), Expected: 100m),
    (Input: new PriceContext(0m, true, RuleMetadata.Empty), Expected: 0m)
};

foreach (var test in cases)
{
    var actual = rule.Execute(test.Input);
    if (actual != test.Expected)
        throw new InvalidOperationException($"期望 {test.Expected}，实际 {actual}");
}

public sealed record PriceContext(decimal Amount, bool IsVip, RuleMetadata Metadata);
```

`ctx` 的类型由 `TContext` 确定，结果类型由 `TResult` 确定。多个输入对象可以组合到同一个上下文中。`RuleId`、`Revision` 记录规则来源，`Fingerprint` 标识源码、类型与相关编译策略。请保存已编译句柄；每次调用 `Compile` 都会重新准备规则。

需要在编辑器显示错误时，读取 `compilation.Success` 和 `compilation.Diagnostics`，其中包含错误码、行列及原始源码跨度；`RequireRule()` 则在编译失败时直接抛出异常。

## 规则可以表达什么

| 能力 | 形式 |
| --- | --- |
| 变量和作用域 | `var`、只读绑定 `const`、局部赋值、嵌套块 |
| 条件和返回 | `if/else`、提前返回；所有可达路径均须返回，允许分支分别返回 |
| 对象结果 | `return new() { ... };`、`return new Decision() { ... };` |
| 动态字段 | `RuleMetadata` 的 `GetString/GetBoolean/GetInt64/GetDecimal/GetObject` 及 `Require*` |
| 宿主方法 | 公开成员与已注册类型的方法，受 `IsMemberAllowed` 策略检查 |
| 编译诊断 | 原始源码的 UTF-16 跨度、行列、位置精度与错误码 |

变量声明必须初始化，语句需要分号，分支需要花括号。`const` 是每次执行时初始化的只读绑定。Metadata 可区分缺失与 null，类型错误不静默转换或默认成零。直接返回的 `new()` 依据具体 `TResult` 推断类型，显式构造类型加入 `ReferencedTypes`。

当前语法不包含循环、异常语句、`async/await`、普通上下文属性赋值或完整 C#；不支持集合直接作为 `TResult`。严格模式暂时拒绝嵌套 Lambda，实验支持通过 `AllowRuntimeLambdaBinding` 显式开启。更多示例见 [Metadata 与对象初始化](docs/Metadata与对象初始化.md)。

## 轻量封装与集成边界

```text
规则源码
  → CRules 识别语句边界与作用域
  → DynamicExpresso 解析表达式、绑定类型与方法
  → 标准表达式树组合
  → 一个 Func<TContext, TResult>
```

CRules 使用 **DynamicExpresso.Core 2.19.6 的公开 API**，不读取上游私有字段，不 fork，也不重新实现其表达式类型系统。Jint 4.17.0 用于基准比较；核心执行不依赖 JavaScript 运行时。

目前面向可信规则作者与受审计宿主。已提供编译限额和成员策略接口；宿主负责可访问成员、输入规模、结果校验、编译缓存及规则版本切换。完整安全沙箱、有界缓存、原子热更新和运行时语句定位仍需后续完善。

NativeAOT 可显式选择 `Interpreted` 后端，已有 Windows x64 标量发布验证；它具有不同的能力与成本边界，不继承上表 JIT 性能结论。详见 [NativeAOT 支持与限制](docs/NativeAOT支持与限制.md)。

## 文档与验证

| 入口 | 内容 |
| --- | --- |
| [文档目录](docs/README.md) | 设计、接入、验证和原始证据 |
| [Metadata 与对象初始化](docs/Metadata与对象初始化.md) | API、错误语义、对象结果与范围 |
| [性能对比报告](docs/性能测试对比.md) | CRules 与 Jint 的完整测试口径和结果 |
| [M0 技术验证](docs/M0技术验证.md) | 公开 API、强类型绑定与实现边界 |
| [开发计划书](docs/CRules_开发计划书_v1.0.docx) | 可编辑的技术设计与后续规划 |

```powershell
dotnet test tests/CRules.Tests -c Release
```

核心验证覆盖分支与作用域、decimal、Metadata、对象结果、诊断、差分和并发输入。基准与验证记录均标明测量范围；新增场景应使用实际规则和上下文另行验证。

## 许可证

本项目采用 [MIT 许可证](LICENSE)。
