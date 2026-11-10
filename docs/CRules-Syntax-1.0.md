# CRules Syntax 1.0

CRules Syntax 1.0 是明确列出的 C# 风格函数体语法，不等于完整支持 C# 9 或其他 C# 版本。通过 `CRulesSyntax.Version` 查询当前版本。宿主 SDK 的 C# 版本与字符串内部语法相互独立。

## 支持清单

| 类别 | 支持范围 |
| --- | --- |
| 基本表达式 | 上游支持的常量、属性、方法、索引、转换、算术、比较、布尔运算、三元表达式及空值合并 |
| 局部变量 | var、初始化、局部赋值、嵌套作用域；const 是每次执行初始化的只读绑定，并非完整 C# 编译期常量 |
| 复合赋值 | 仅局部变量的 +=、-=、*=、/=、%=；遵循上游运算和转换规则 |
| 条件 | if/else、else if，支持单条语句与花括号代码块 |
| 返回 | 每条可达结束路径必须显式 return；无隐式末表达式返回 |
| switch 语句 | 常量 case、分组标签、default、嵌套 switch、break |
| switch 表达式 | 常量 => 表达式，最后必须有 _ => 表达式；允许尾逗号 |
| 对象 | 显式构造与成员初始化，直接返回的目标类型 new() |
| 集合结果 | 物化一维数组、一维交错数组与 List<T>；不是任意 IEnumerable 或惰性序列 |
| 异步结果 | 返回宿主产生的 Task/ValueTask，不支持函数体内 await |

嵌套 Lambda/LINQ 仍需实验选项 AllowRuntimeLambdaBinding；不宣称严格无运行时绑定。类型和方法能力继承 DynamicExpresso 的支持范围，不能等同于完整 C# 重载、泛型和类型系统。

## switch 语句

```csharp
var rate = 1m;
switch (level) {
    case "gold":
    case "platinum":
        rate *= 0.8m;
        break;
    case "silver":
        return 0.9m;
    default:
        break;
}
return rate;
```

选择值支持字符串、整数、字符、枚举、布尔、decimal 及适用的 nullable 类型。case 仅支持字面常量与枚举常量；不执行宿主方法求 case 值。各分支拥有独立局部作用域，选择值只求值一次。标签可以分组，但有语句的分支必须以 return 或 break 结束，不能隐式贯穿。break 退出最近的 switch。

无 default 时通常仍需覆盖未命中路径；如果选择表达式本身为可识别常量，或 bool/bool? 的 true、false 与适用的 null 已全部覆盖，可以证明路径完整。有 break 的 switch 仍需覆盖后续返回路径。所有可达分支都 return 时，后面的语句不可达。普通枚举的已命名成员不能代表其全部取值。

case 常量必须能隐式转换到选择值类型；例如 int switch 不接受 double 标签，byte switch 不接受 256。编译阶段拒绝这些写法，不截断或环绕标签值。

## switch 表达式

```csharp
return level switch {
    "gold" => 0.8m,
    "silver" => 0.9m,
    _ => 1m
};
```

支持作为整条 return 表达式、局部变量初始化或赋值的值。分支结果类型交给上游条件表达式规则统一，选择值只求值一次，仅选中的结果求值。必须用最后的 `_` 覆盖剩余情况。不支持将 switch 嵌入任意算术、参数列表或 Lambda 中，也不支持模式、when、or、属性模式、列表模式或解构。

## 单行条件与集合返回

```csharp
if (amount < 0m) return 0m;
return amount;
```

else 绑定最近的 if；分支内声明局部变量需要花括号。简单布尔常量条件可参与返回路径检查；这不是完整 C# 编译期常量折叠。

局部复合赋值不能修改形参、只读变量或输入对象属性。预定义数值运算需要符合隐式转换约束；小整数允许合法复合赋值的回转转换，例如 `var x = (byte)250; x += 10;` 按非 checked 语义得到 4。`var x = 1; x += 1.5;` 与 `char` 加 int 则被拒绝。只读 `const` 绑定不提供 C# 编译期常量转换语义。

```csharp
// TResult 为 int[]
return new int[] { 1, 2, 3 };
```

```csharp
// TResult 为 List<decimal>，宿主在 ReferencedTypes 注册 typeof(List<>)
return new List<decimal>() { 1m, 2m };
```

也支持 `new[] { 1, 2, 3 }`、`new int[length]`、`new int[3] { 1, 2, 3 }`、一维交错数组及 List 的单元素花括号初始化。隐式数组的共同元素类型通过上游条件表达式绑定确定；显式数组逐项检查转换。带初始化列表时长度必须为匹配元素数量的常量。`null` 元素、数值提升、元素顺序与数组创建异常都有差分回归。

隐式数组与带长度数组的创建适配覆盖整条返回值、局部变量值、赋值值和对象初始化成员值；不承诺嵌入任意其他表达式位置的完整 C# 解析。多类型参数的泛型创建表达式可用括号包住，避免其逗号与分支/元素分隔符混淆。

## 明确不支持

不支持集合表达式 `[1, 2]` 与展开元素 `..values`、多维数组创建、字符串插值、原始字符串、空条件访问、++/--、显式类型局部声明、循环、try/catch/finally、using、lock、goto、yield、局部函数、递归、类型声明及 unsafe。嵌套目标类型 new() 不在本轮范围。代码中的 `const` 只读语义是清单内明确的差异，不能据此推断完整 C# 常量行为。

遇到不支持的语法返回编译诊断，不自动切换 Roslyn 或更高版本解析器。宿主文档中的原始字符串和 `["a", "b"]` 参数配置写法是外层 C# 示例，不意味着代码字符串内支持相同语法。

官方用例来源、差分方式、覆盖数量与排除项见 [Roslyn 兼容性验证](Roslyn兼容性验证.md)。
