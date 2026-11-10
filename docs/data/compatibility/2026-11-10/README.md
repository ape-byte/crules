# 多目标兼容性验证记录

本批验证对应 netstandard2.0、net6.0、net8.0、net10.0 四组库资产。精确源码 SHA-256、测试计数和验证范围见 [verification.json](verification.json)。完整单元测试的复现命令见 [框架兼容性](../../../框架兼容性.md)。TRX 的摘要和原文件哈希保存在 JSON 中；不重复提交数 MB 的逐项 TRX。

`consumers/` 保存独立包消费项目和原始运行输出，只有 PackageReference，没有源码项目引用。每个项目分别执行 JIT 和解释模式各 6 项检查。以 net6.0 为例，在仓库根目录打包后运行：

```powershell
dotnet pack src/CRules/CRules.csproj -c Release
dotnet restore docs/data/compatibility/2026-11-10/consumers/net6.0/Matrix.csproj --source ./publish/packages --source https://api.nuget.org/v3/index.json --packages .tmp/compatibility-packages --force-evaluate
dotnet build docs/data/compatibility/2026-11-10/consumers/net6.0/Matrix.csproj -c Release --no-restore
.tmp/dotnet6/dotnet.exe docs/data/compatibility/2026-11-10/consumers/net6.0/bin/Release/net6.0/Matrix.dll
```

其他目标替换目录和目标框架，使用对应运行时；net472 直接运行 Matrix.exe。本机 net472 消费验证实际使用已安装的 .NET Framework 4.8.09221，不能据此宣称原始 4.7.2 运行时已验收。相同预览版若在本地重新打包，应换新缓存路径，避免缓存旧包。

`aot/` 保留两个平台的 publish 原始警告和执行输出。Windows 使用安装包消费项目，Linux 使用源码项目引用；二者均为 .NET 10、解释模式，21 项必测检查全部通过，动态代码能力为 false。输出中的 Expando 探针、实验 LINQ 探针和简单计时不是额外的语法支持承诺或正式性能基准。本轮验证不扩大旧目标框架的 AOT 承诺；详细保留配置和限制见 [NativeAOT支持](../../../NativeAOT支持.md)。
