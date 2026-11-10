# Roslyn 测试来源与移植说明

本目录的测试片段部分改编自 .NET Foundation 的 [dotnet/roslyn](https://github.com/dotnet/roslyn)，固定提交为 `636a46f73c2006fad15339a5ba8c9ac5cd1c10c8`。版权归 .NET Foundation and Contributors 所有，按 [MIT 许可证](LICENSE.txt) 使用。

- `upstream.json`：12 个官方测试文件的完整路径、固定版本链接与规范化源码 SHA-256。
- `inventory.json`：这 12 个文件中 1,719 个 `[Fact]`/`[Theory]` 测试方法的盘点，以及可移植核心数量和未直接提取的原因。
- `cases.json`：自动提取的 267 个独立函数核心或集合创建表达式；每条记录有原文件、测试方法、行号、代码、签名和移植说明。
- `curated.json`：18 个手工整理的片段或边界变体，包括局部复合赋值、默认结构体构造、目标类型 new 和闭合 bool? 值域的 switch。
- `negative.json`：14 个错误语义片段。Roslyn 必须报告错误，CRules 必须返回编译诊断。

每个正向片段都由 Roslyn 5.0.0 使用 C# 9 语义编译为测试参照，再由 CRules 的 JIT 与 Interpreted 两种模式执行相同代码。参数签名相同，对多个输入和数值边界比较返回值或异常类型。C# 9 只是测试参照的版本，CRules 的语言范围仍是 Syntax 1.0。

移植保留规则核心：显式局部类型改为 `var` 加同类型转换；局部自增减语句等价改写为 `+=/-= 1`；可写形参复制到局部；删除 Console 输出、不可达尾句和 IL 断言。集合用例直接返回原创建表达式，替代 Console/LINQ 输出验证。手工变体的改动单独记录，不将它们冒充原测试全文。

原文件里的编译器诊断格式、语言版本开关、IL 排布、源生成、泛型约束、原夹具依赖，以及 Syntax 1.0 之外的循环、异常、模式等，不作为 CRules 完整实现承诺。`inventory.json` 的未提取项不是执行成功或跳过的用例；本目录也不表示整个 Roslyn 官方测试集已经通过。

## 重建自动提取数据

在仓库根目录执行，需 Python 3 和 .NET 10。下载只在重建时发生，日常测试离线读取已提交的数据：

```powershell
python tools/RoslynImport/download.py
dotnet restore tools/RoslynImport/RoslynImport.csproj --locked-mode
dotnet run --project tools/RoslynImport -c Release --no-restore
```

生成位置是 `.tmp/roslyn-import-output/`，不会自动覆盖本目录。审查生成差异后更新 `cases.json`、`inventory.json` 与 `upstream.json`，保留手工片段，再运行：

```powershell
dotnet test tests/CRules.Tests -c Release --filter FullyQualifiedName~RoslynCompatibilityTests
```
