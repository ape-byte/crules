# 性能对比与回退复测

当前主报告：[文字报告](../docs/性能报告.md)。

## 三种计时口径

1. **完整循环**：每次新建随机 context，计入传参、计算、校验与分配。CRules、同构原生 C# 各 5 个独立进程；三种 Jint 复用入口各 3 个进程，每进程 100,000 次。旧核心和新核心交替执行。
2. **编译准备**：固定源码、复用编译器、预热 5 次后，重复得到委托；排除 context 构建、源码生成与执行。各版本各场景各 5 个进程，简单规则每进程 200 次、复杂规则 20 次。
3. **微基准**：BenchmarkDotNet ShortRun，重跑原有 23 组、当前版本新增的 4 组调用 API，并补 8 组相同展开算法的纯规则调用。纯规则组使用 3 个独立进程、每进程 5 次测量。decimal 字符串 helper、实验 Lambda 与每次新建 Jint Engine 的结果分别解释。

完整循环与编译准备固定逻辑核心 2（affinity mask 4），设置 `DOTNET_TieredCompilation=0`，预热至少 2,000 次且不少于 750 ms。BDN 同样固定核心，但使用默认 JIT 策略及自身预热。两类数据不直接混合。

原生同构方法由规则源码生成、禁用方法内联，并经同样的 Func 委托调用。独立循环参考算法生成期望值。各实现逐项校验输出，计时结束后再比对版本和引擎间的校验和。

## 复现完整循环与编译准备

从仓库根目录执行，使用 .NET 10 与 Python 3。所有计时进程串行运行，期间不要同时构建或启动另一批基准。

```powershell
dotnet restore benchmarks/CRules.Benchmarks --locked-mode
dotnet build benchmarks/CRules.Benchmarks -c Release --no-restore
# 0 轮仅导出输入、规则和期望值，不宣称执行测试。
./benchmarks/CRules.Benchmarks/bin/Release/net10.0/CRules.Benchmarks.exe --business-suite .tmp/performance-repeat/business-100k 100000 0
python benchmarks/reporting/generate-native-equivalent.py .tmp/performance-repeat/business-100k/rules benchmarks/CRules.Benchmarks/NativeBusinessEquivalent.cs
python benchmarks/reporting/prepare-baseline.py fda9f49 .tmp/perf-baseline-repeat
dotnet restore .tmp/perf-baseline-repeat/benchmarks/CRules.Benchmarks --locked-mode
dotnet build .tmp/perf-baseline-repeat/benchmarks/CRules.Benchmarks -c Release --no-restore
dotnet build benchmarks/CRules.Benchmarks -c Release --no-restore
python benchmarks/reporting/run-comparison.py .tmp/perf-baseline-repeat/benchmarks/CRules.Benchmarks/bin/Release/net10.0/CRules.Benchmarks.Before.exe benchmarks/CRules.Benchmarks/bin/Release/net10.0/CRules.Benchmarks.exe .tmp/performance-repeat
```

`prepare-baseline.py` 只导出指定提交的旧核心，不切换工作分支。旧核心搭配当前相同的 benchmark 源码，仅通过条件编译适配旧公共 API。原始 worker 数据、汇总数据和程序集 SHA-256 保存在输出目录。输入样本与期望值在计时前生成，实际 context 在计时内构造。

## BenchmarkDotNet 与报告

分别在旧、新版本的 `benchmarks/CRules.Benchmarks` 目录中运行，确保 BDN 使用对应的项目。`--artifacts` 请传绝对路径，避免原始结果落入临时快照。旧版示例（快照项目/程序集追加 Before，避免 BDN 在仓库子目录找到两个同名项目）：

```powershell
# 从 .tmp/perf-baseline-repeat/benchmarks/CRules.Benchmarks 运行
$env:CRULES_BENCH_PROVENANCE_DIR = 'ABSOLUTE_OUTPUT/bdn/before/provenance'
./bin/Release/net10.0/CRules.Benchmarks.Before.exe --filter '*RoutingBenchmarks*' '*AccountingBenchmarks*' '*CollectionBenchmarks*' '*CompilationBenchmarks*' '*HostLifecycleBenchmarks*' '*InvocationApiBenchmarks*' --job short --exporters json --affinity 4 --keepFiles --artifacts ABSOLUTE_OUTPUT/bdn/before
```

当前项目使用 `bdn/current` 路径和 `CRules.Benchmarks.exe`。原有 23 组及新 API 使用 ShortRun 的单进程三次测量；对 BusinessRuleHotBenchmarks 另用 `--filter "*BusinessRuleHotBenchmarks*" --job short --launchCount 3 --warmupCount 3 --iterationCount 5`，结果仍写入对应版本的相同 artifacts 目录。环境变量使基准在准备阶段记录实际加载的核心与 benchmark 程序集路径、哈希，配合 IL/签名指纹校验避免误测同一份核心。运行结束后清除该环境变量。记录 CPU 信息到输出目录的 `cpu.json`（Name、NumberOfCores、NumberOfLogicalProcessors），最后生成报告：

```powershell
dotnet build benchmarks/reporting/CoreAssemblyProof -c Release
python benchmarks/reporting/collect-assembly-proof.py BASELINE_EXE CURRENT_EXE .tmp/performance-repeat
python benchmarks/reporting/build-performance-report.py .tmp/performance-repeat
```

报告严格要求 141 条完整循环记录、50 条编译记录、旧版 31 / 当前 35 个成功 BDN 结果；缺失或校验不一致会失败。所有数字都保留计时范围与样本数，历史数据不会被覆盖。BDN 的新构建可能改变 PDB 路径和 MVID，二进制 SHA 不必等于直接构建；使用 CoreAssemblyProof 与 collect-assembly-proof.py 校验实际加载核心的 IL/签名指纹与对应版本一致。报告生成器仅输出 Markdown、JSON 和 CSV。

报告生成器只向指定输出目录写入测量数据和 generated-report.md，不覆盖手工维护的 docs 文档或仓库 README。正式归档数据位于 docs/data/performance/2026-11-10。
