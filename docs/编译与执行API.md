# 编译与执行 API

CRules 接收函数体代码、输入签名和返回类型，编译后得到 executable。无需规则 ID 或版本；可选的 Name 只描述函数名称，不注册全局函数、不提供递归或版本覆盖。

## 完整配置入口

```csharp
using CRules;

var compiler = new RuleCompiler(new CompilerOptions { DecimalOnly = true });
var executable = compiler.Compile(new CompileOptions
{
    Code = "return amount * rate;",
    Name = "Calculate",
    ReturnType = typeof(decimal),
    Parameters = [new("amount", typeof(decimal)), new("rate", typeof(decimal))]
}).RequireExecutable();

var result = (decimal)executable.Execute(100m, 0.9m)!; // 90
```

`CompileOptions` 是一次编译的代码与签名；`CompilerOptions` 是编译器的数值策略、允许成员、编译限额和执行模式。参数定义在编译时复制，executable 不依赖调用方后续修改参数列表。

## 常用泛型重载

```csharp
var single = compiler.Compile<decimal, decimal>(
    "return amount * 0.9m;", parameterName: "amount").RequireExecutable();
decimal first = single.Execute(100m);

var multiple = compiler.Compile<Func<decimal, decimal, decimal>>(
    "return amount * rate;", ["amount", "rate"], name: "Calculate").RequireExecutable();
decimal second = multiple.Execute(100m, 0.9m);

var noArguments = compiler.Compile<Func<decimal>>(
    "return 1m;", []).RequireExecutable();
```

泛型入口保留具体委托类型，Execute 直接调用该委托。运行时 Type 入口使用已编译的参数数组适配器，返回 object，并校验参数数量、类型与 null；它会有数组、装箱和适配成本。两条路径都不在执行时使用 DynamicInvoke 或反射调用。

输入名必须唯一、合法且不占用语法关键字、注册类型名或内部 `__crules_` 前缀。形参只读，局部变量不能遮蔽形参。单输入默认名 ctx，可自定义；多输入没有固定 ctx。暂不支持 void、ref/out/in、指针和 ref struct 签名。

## 直接传入动态对象

```csharp
using System.Dynamic;

dynamic customer = new ExpandoObject();
customer.IsVip = true;
customer.Discount = 0.9m;

var dynamicCompiler = new RuleCompiler();
var dynamicExecutable = dynamicCompiler.Compile<Func<decimal, ExpandoObject, decimal>>(
    "if(customer.IsVip) return amount * customer.Discount; else return amount;",
    ["amount", "customer"]).RequireExecutable();

decimal result = dynamicExecutable.Execute(100m, (ExpandoObject)customer); // 90
```

ExpandoObject 是直接动态绑定，字段缺失或类型错误在执行时失败。字典保持索引器用法，JsonElement 保持其读取 API，不自动改成点访问。DataObject 是可选的 JSON 快照工具，可用作整个输入或任意字段，与特定业务字段无关。动态输入和强类型输入的性能需要分别测量。

DecimalOnly 下，动态算术应先把参与运算的值显式转换成 decimal；动态浮点值会被运行时检查拒绝。为避免静态成员白名单被动态访问绕过，配置 IsMemberAllowed 时拒绝动态绑定；需要精确成员审计的宿主应使用有类型输入。ExpandoObject 不被复制或冻结，并发修改由宿主协调。自定义 DynamicObject 不在本轮验收范围。

## 异步返回值

```csharp
var asyncExecutable = compiler.Compile<MyService, Task<decimal>>(
    "return ctx.CalculateAsync();").RequireExecutable();

// 强类型委托，保留 Task<decimal> 的返回类型。
decimal value = await asyncExecutable.Execute(service);

// 通用异步入口，会等待任务并取出结果，返回 ValueTask<object?>。
object? boxedValue = await asyncExecutable.ExecuteAsync(service);
```

支持 Task、Task<T>、ValueTask、ValueTask<T>，任务异常和取消原样传播，非泛型任务完成返回 null。同步返回值也可通过 ExecuteAsync 调用，计算在当前线程执行，不自动 Task.Run。ValueTask 返回值每次执行只消费一次。

`ExecuteAsync(object?[] arguments, CancellationToken cancellationToken)` 在调用前检查取消；它不强制中断已经开始的计算或 I/O。若异步宿主方法需要取消，把 CancellationToken 声明成普通输入参数并传给该方法。代码块内 await 语法不在 Syntax 1.0 中。

## 错误与执行模式

代码错误返回 Compilation.Diagnostics，包含错误码、原始 UTF-16 跨度、行列和位置精度；RequireExecutable 在失败时抛出异常。缺少代码、签名为空等调用错误抛参数异常。运行时错误向调用方传播，不转成零或默认结果。

JIT 与 Interpreted 都有本轮回归。历史 NativeAOT 标量发布结果不覆盖本轮新的签名适配、动态绑定和异步桥接；本轮未重新验证 NativeAOT 发布，也未重跑性能基准。
