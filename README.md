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
| 编译后重复执行 | JIT 模式将整份规则编译为一个具体类型委托，热执行不重复解析源码 |
| decimal 数值策略 | 十进制运算可直接使用 `decimal`，支持编译期拒绝 `float/double` 运算 |
| 可扩展上下文 | 支持多个命名输入、普通对象与 ExpandoObject；DataObject 是可选数据工具 |
| 适合 AI 生成 | 规则、输入和预期输出均有明确契约，方便 AI 生成、修改并交给同一引擎验证 |

## 让 AI 生成规则并验证结果

AI 时代，需求可以直接转成规则配置。AI 根据业务描述和上下文契约编写规则，再构造多组 mock 输入与预期结果，使用 CRules 编译和执行，检查正常、分支、边界及缺失数据场景。

```text
需求与上下文契约 → AI 生成规则与 mock 用例 → 编译和执行验证 → 宿主加载已验证规则
```

这让应用可以在稳定的执行接口上持续适应新需求。CRules 提供规则编译与执行能力；AI 调用、用例组织和配置发布由宿主应用接入。预期结果应依据业务约定确定，避免仅用待验证规则本身反算答案。

## 与 Jint 的简要对比

**以下为 2026-11-10 的版本实测数据（CRules 99dda85），未重测之后的字典/AOT 改动。** 每次新建随机 context，计算 100,000 次并逐项校验。CRules 与同构原生 C# 各取 5 个进程中位数，Jint 取 3 个进程中位数。为统一入口，下表始终使用复用一个 Engine 的 **Jint 函数 + CLR 对象** 调用；JSON、SetValue 和准备成本另列在完整报告。

| 场景 | CRules | Jint 函数 + CLR | 原生 C# 同构委托 | Jint / CRules 耗时比 |
| --- | ---: | ---: | ---: | ---: |
| 小 context / 简单 | 24.05 ms | 378.05 ms | 25.76 ms | **15.72×** |
| 小 context / 100 变量 | 39.72 ms | 7,372.23 ms | 39.07 ms | **185.61×** |
| 小 context / 100 变量 + 分支 | 39.68 ms | 8,408.18 ms | 37.49 ms | **211.89×** |
| 大 context / 简单 | 961.79 ms | 1,447.91 ms | 1,020.87 ms | **1.51×** |
| 大 context / 100 变量 + 分支 | 988.62 ms | 25,798.13 ms | 1,000.84 ms | **26.09×** |

本轮五种已有规则未确认超过 5% 的强类型热执行回退，但这不等于证明零回退。 S1, S2, S3, S4, S5 的区间仍覆盖 +5%，测量不确定性需要保留。 补充纯调用中部分原生对照也明显漂移，相关归一化值不能用来断言重构导致回退或提升。

编译准备的配对中位变化为 -1.5%～+60.3%。短规则 BDN 编译微基准为 0.734 → 1.149 ms（+56.5%），存在准备成本增长。这部分属于加载或规则变更成本，不能用热执行优势掩盖；应编译后复用。

本轮普通规则使用安全整数/定点语义，没有为 Jint 叠加 decimal 转换成本。相同代码结构的原生 C# 委托作为性能基线；context 构建、输入方式和机器波动都会改变收益，以上不是统一性能保证。

| 关注点 | CRules | Jint |
| --- | --- | --- |
| 执行方式 | 有限 C# 风格函数体，编译后复用委托 | JavaScript 运行时，复用 Engine 或按需隔离 |
| 十进制计算 | 原生 decimal 与 DecimalOnly 策略 | 默认 Number 为二进制浮点，精确十进制需额外设计 |
| 调用成本 | 强类型与运行时 Type 入口分别测量 | CLR 对象、SetValue 和 JSON 入口分别测量 |

业务探针统一固定逻辑核心、禁用分层编译；微基准使用默认 JIT 策略。完整环境、轮次范围、内存、重构前后区间和复现步骤见 [性能对比报告](docs/性能报告.md)。

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

var compiler = new RuleCompiler(new CompilerOptions { DecimalOnly = true });
var executable = compiler.Compile<Func<decimal, decimal, decimal>>(
    code: """
        var amount = price;
        amount *= discount;
        if (amount < 0m) return 0m;
        return Math.Round(amount, 2, MidpointRounding.ToEven);
        """,
    parameterNames: ["price", "discount"],
    name: "CalculatePrice").RequireExecutable();

Console.WriteLine(executable.Execute(100m, 0.9m)); // 90

// 预期输出来自业务约定，可由 AI 生成 mock 后交给宿主验证。
foreach (var test in new[] { (100m, 0.9m, 90m), (100m, 1m, 100m), (0m, 0.9m, 0m) })
{
    var actual = executable.Execute(test.Item1, test.Item2);
    if (actual != test.Item3) throw new InvalidOperationException("输出与期望不一致。");
}
```

支持配置类 `Compile(CompileOptions)`、单输入 `Compile<TInput, TResult>` 和多输入 `Compile<TDelegate>`。形参分别命名；单输入默认名称为 `ctx`，可改名。executable 名称可选，仅作函数描述，不承担规则存储或版本管理。代码直接传入字符串。

`Execute` 在泛型入口上是有类型委托。运行时 Type 构建使用参数数组适配，额外承担数量／类型检查和装箱成本。`ExecuteAsync` 可等待 Task/ValueTask 返回值；同步结果直接完成，不自动调度线程。更完整的配置、动态对象与异步示例见 [编译与执行 API](docs/构造与执行.md)。

## 规则可以表达什么

| 能力 | 形式 |
| --- | --- |
| 变量和作用域 | `var`、带初始化值的显式类型声明、`List a = new() { ... };` 元素类型推断、只读绑定 `const`、局部赋值、嵌套块 |
| 条件和返回 | `if/else`、单行 if、两种基础 switch、提前返回；所有可达路径必须返回 |
| 对象结果 | `return new() { ... };`、`return new Decision() { ... };`、`Decision a = new() { ... };` |
| 可空结果 | 引用类型与 `Nullable<T>` 支持 `return null;` 或返回可空输入、属性、局部变量 |
| 字典 | Dictionary 两种初始化器、嵌套 new()、索引读写/复合赋值、常用方法和 out 查询；可直接返回字典 |
| 类型可见性 | 输入、返回类型及其泛型实参/数组元素自动可见，ReferencedTypes 只补充额外类型 |
| 动态输入 | 直接使用 ExpandoObject 点访问，或使用可选 DataObject 有类型读取 |
| 宿主方法 | 公开成员与已注册类型的方法，受 `IsMemberAllowed` 策略检查 |
| 编译诊断 | 原始源码的 UTF-16 跨度、行列、位置精度与错误码 |

语法范围标为 **CRules Syntax 1.0**，不宣称完整支持某个 C# 版本。支持局部 `+=`、`-=`、`*=`、`/=`、`%=`、物化数组/List 返回、常量 switch 语句和表达式。模式匹配、集合表达式 `[1, 2]`、展开元素、插值字符串、循环与代码块内 await 均不在 1.0 范围。严格模式暂时拒绝嵌套 Lambda，实验支持通过 `AllowRuntimeLambdaBinding` 开启。

详细范围、示例与错误规则见 [CRules Syntax 1.0](docs/语法参考.md)。宿主项目代码可以使用自身 SDK 支持的语法；字符串内部只遵循 CRules 的支持清单。

局部变量可以直接写 `int count = 2;`、`long total = 100L;`、`float rate = 0.5f;`、`decimal amount = 100m;` 等声明；每条声明只定义一个变量且必须初始化。`List a = new() { amount, rate };` 是 CRules 的专有简写，从元素推断 `List<T>`；List<> 已出现在签名中时无需手动注册，否则通过 ReferencedTypes 补充。空列表或元素无法确定共同类型时应显式写 `List<T>`。`DecimalOnly` 模式仍禁止 float/double。可空引用注解不提供完整 C# nullable 流分析；非可空值类型不能返回 null。

字典可以直接构造和返回，无需重复注册返回类型：

```csharp
var dictionaryExecutable = new RuleCompiler()
    .Compile<Func<decimal, decimal, Dictionary<string, decimal>>>("""
        Dictionary<string, decimal> result = new() {
            ["amount"] = amount,
            ["rate"] = rate
        };
        result["amount"] *= rate;
        result.Add("extra", 1m);
        return result;
        """, ["amount", "rate"]).RequireExecutable();

var values = dictionaryExecutable.Execute(100m, 0.9m); // amount = 90m
```

字典还支持 `{ { "key", value } }` 的 Add 初始化形式，重复键会抛错；索引初始化 `["key"] = value` 则覆盖已有值。支持 TryGetValue、Remove 的 out 局部变量；详细范围和边界见 Syntax 1.0，未宣称支持完整 C# 字典相关语法。

## 轻量封装与集成边界

```text
规则源码
  → CRules 识别语句边界与作用域
  → DynamicExpresso 解析表达式、绑定类型与方法
  → 标准表达式树组合
  → 一个具有命名输入签名的委托
```

CRules 使用 **DynamicExpresso.Core 2.19.6 的公开 API**，不读取上游私有字段，不 fork，也不重新实现其表达式类型系统。Jint 4.17.0 用于基准比较；核心执行不依赖 JavaScript 运行时。

目前面向可信规则作者与受审计宿主。已提供编译限额和成员策略接口；宿主负责可访问成员、输入规模、结果校验、编译缓存及规则版本切换。完整安全沙箱、有界缓存、原子热更新和运行时语句定位仍需后续完善。

NativeAOT 必须显式选择 `Interpreted` 后端。2026-11-10 已在 Windows/Linux x64 真实发布并运行，各通过 21 项检查，覆盖字典、命名多参数、对象/空值、异步结果与有类型 JSON 读取。宿主需保留所用 DTO、闭合泛型集合和方法的元数据及必要原生代码；此模式不继承上表 JIT 性能结论，也不宣称任意动态代码均兼容。详见 [NativeAOT 支持与限制](docs/NativeAOT支持.md)。

## 文档与验证

NuGet 预览包的构建、依赖、本地安装与发布验证见 [NuGet 构建与发布](docs/NuGet%E5%8F%91%E5%B8%83.md)。

| 入口 | 内容 |
| --- | --- |
| [快速开始](docs/快速开始.md) | 项目引用、第一条规则和多个参数 |
| [构造与执行](docs/构造与执行.md) | 三种 Compile 入口、配置、上下文、异步和诊断 |
| [语法参考](docs/语法参考.md) | 声明、赋值、流程、集合、动态数据和明确边界 |
| [已知问题](docs/已知问题.md) | 当前缺陷及对应解决方式 |
| [NativeAOT 支持](docs/NativeAOT支持.md) | Windows/Linux 真实验证及宿主要求 |
| [三方性能报告](docs/性能报告.md) | 原生 C#（Roslyn 构建）、Jint、CRules 实测 |
| [与 Jint 对比测试](docs/与Jint对比测试.md) | 输入入口、数值语义、准备/分配口径 |

```powershell
dotnet test tests/CRules.Tests -c Release -f net10.0
```

核心验证覆盖分支与作用域、decimal、动态数据、对象结果、诊断、差分和并发输入。另从 Roslyn 官方测试提取支持范围内的函数核心，逐条与 Roslyn 编译结果比较，覆盖 JIT 与 Interpreted。基准与验证记录均标明测量范围；新增场景应使用实际规则和上下文另行验证。

## 许可证

本项目采用 [MIT 许可证](LICENSE)。

支持的目标框架与跨版本验证见 [框架兼容性](docs/%E6%A1%86%E6%9E%B6%E5%85%BC%E5%AE%B9%E6%80%A7.md)。
