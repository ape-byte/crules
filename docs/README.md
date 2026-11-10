# CRules 文档

项目入口见 [仓库 README](../README.md)。本目录保存开发计划、实现边界、性能结果和验证证据。

产品口号：**以不变应万变**。正式灰橙图标与资源说明见 [视觉标识](branding/README.md)。

## 设计与接入

当前 API 入口见 [编译与执行](编译与执行API.md)，语言范围见 [CRules Syntax 1.0](CRules-Syntax-1.0.md)。旧验证报告和 Word 计划书保留历史证据与规划，不代表当前 API。

| 文档 | 内容 |
| --- | --- |
| [编译与执行 API](编译与执行API.md) | 配置类、命名形参、Type 签名、动态对象与异步返回值 |
| [CRules Syntax 1.0](CRules-Syntax-1.0.md) | 当前语法清单、示例及明确限制 |
| [Roslyn 兼容性验证](Roslyn兼容性验证.md) | 官方测试来源、移植盘点、差分方法及本轮修复 |
| [动态数据与对象初始化](动态数据与对象初始化.md) | 有类型动态字段读取、目标类型 new、初始化审计、所有路径返回及本轮验收 |
| [开发计划书 v1.0](CRules_开发计划书_v1.0.docx) | 可编辑 Word 计划书，包含语法、架构、里程碑、验收与风险；实现状态以当前源码和验证文档为准 |
| [M0 技术验证](M0技术验证.md) | 公开 API 路线、局部变量绑定、执行边界及未关闭技术闸门 |
| [M0 实施交付记录](M0实施交付记录.md) | 初始交付范围和验证记录 |
| [NativeAOT 支持与限制](NativeAOT支持与限制.md) | 显式解释后端、元数据保留、实际发布测试和能力限制 |

## 性能与内存

| 文档 | 内容 |
| --- | --- |
| [性能测试对比：文字版](性能测试对比.md) | 推荐阅读入口；五种随机新建 context 场景，Jint 单例与隔离模式，时间、分配、GC、工作集、准备成本及复现方法 |
| [重构后性能交互报告](performance/2026-11-10/index.html) | 重新设计的离线 HTML；明确划分 CRules / Jint / 原生 C#，包含配对回退与准备成本 |
| [历史批次自动分析](performance/2026-11-09/business-100k/分析报告.md) | 2026-11-09 原始记录生成的详细表格 |
| [早期微基准报告](performance/2026-11-09/性能测试报告.md) | BenchmarkDotNet 原生 C#、CRules、Jint 标量／集合和生命周期微基准；计时口径与端到端报告不同 |

## 原始证据

- [主测记录 results.json](performance/2026-11-09/business-100k/results.json)：96 条进程记录，包含时间、内存、GC 和校验和。
- [CSV measurements.csv](performance/2026-11-09/business-100k/measurements.csv)：适合表格工具分析的主测记录。
- [准备成本 preparation.json](performance/2026-11-09/business-100k/preparation.json)：60 条独立准备成本记录，实际次数为每条 200 或 1000 次。
- [测试清单 manifest.json](performance/2026-11-09/business-100k/manifest.json)：环境、种子、场景、输入覆盖与程序集哈希。
- [单例补测说明](performance/2026-11-09/business-100k/singleton-supplement.json)：SetValue 单例入口的补充批次信息。
- [规则与 context 样例](performance/2026-11-09/business-100k/rules/)：CRules、JavaScript 规则及 JSON 输入样例。
- [工作负载验证记录](validation/2026-11-09/business-workloads/verification.json)：主测校验、核心回归结果与 HTML 检查记录。
- [Syntax 1.0 重构验证记录](validation/2026-11-10/syntax-1.0/verification.json)：720 项回归、Roslyn 兼容检查、构建和文档示例验收。
- [重构后性能复测记录](validation/2026-11-10/performance/verification.json)：前后核心、1,350 万次输出校验、66 个微基准结果、程序集指纹与新 HTML 验收。

所有性能数字应与场景、计时范围和引擎生命周期一起阅读。结构体 context 的收益目前只有讨论和估算，未纳入实测结果。
