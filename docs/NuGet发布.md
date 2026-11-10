# NuGet 构建与发布

仓库当前配置为 **0.1.0-preview.1**，目标框架为 **net10.0**。这是本地发布准备，不代表已经上传到 nuget.org 或创建正式版。

## 包的划分

| 包 | 用途 | 包依赖 |
| --- | --- | --- |
| CRules | RuleCompiler、Syntax 1.0 和执行实现 | 同版本 CRules.Abstractions、DynamicExpresso.Core 2.19.6 |
| CRules.Abstractions | 编译/执行契约、诊断和 DataObject | 无额外包依赖 |

一般使用者只需要引用 CRules，Abstractions 会传递安装。Jint、Roslyn、测试、基准程序和本地 tools 不会打入生产包。

元数据在 [NuGet.props](../NuGet.props)：版本、MIT 许可证、图标、仓库地址和符号包。每个包带独立 README、LICENSE、图标、net10.0 DLL、XML 文档，snupkg 保存符号。包用 README 说明 AOT 条件和预览版已知问题。

## 构建

在仓库根目录使用满足 global.json 的 SDK：

```powershell
dotnet restore CRules.slnx --locked-mode
dotnet test tests/CRules.Tests -c Release --no-restore
dotnet pack src/CRules.Abstractions/CRules.Abstractions.csproj -c Release -o artifacts/nuget/0.1.0-preview.1
dotnet pack src/CRules/CRules.csproj -c Release -o artifacts/nuget/0.1.0-preview.1
```

输出两个 nupkg 和两个 snupkg。`artifacts/` 已忽略。默认版本在 NuGet.props；临时覆盖版本时，对两个包使用相同 `-p:Version=...` 和匹配的输出目录，避免核心包引用不存在的 Abstractions 版本。

## 发布前验证

1. 检查 ID、版本、net10.0、MIT、README、图标和仓库提交。
2. 检查依赖：核心只引入 Abstractions 和 DynamicExpresso，Abstractions 没有包依赖。
3. 新建独立 net10.0 消费项目，只用 PackageReference，从本地目录安装，不使用 ProjectReference。
4. 验证强类型/Type 入口、字典、对象、空值、DataObject、异步结果；核对 JIT 和 Interpreted。
5. AOT 能力在安装包的消费项目上真实 publish 并运行；普通 test 不能替代。保留配置见 [NativeAOT支持](NativeAOT%E6%94%AF%E6%8C%81.md)。

本地安装示例，把来源替换成实际绝对输出目录：

```powershell
dotnet add MyApp/MyApp.csproj package CRules --version 0.1.0-preview.1 --source ABSOLUTE_PACKAGE_DIRECTORY
```

还原传递依赖时同时配置本地目录和 nuget.org。使用独立缓存验证，避免旧的同版本本地包掩盖重打包内容。正式发布的同一版本不能覆盖，修改内容应使用新版本。

## 上传

先确定版本、目标源和发布权限。包 ID 查询不到，不代表一定可注册或当前账号有权限。以下只是命令示例，尚未执行；密钥通过环境变量注入，不写入仓库：

```powershell
dotnet nuget push artifacts/nuget/0.1.0-preview.1/CRules.Abstractions.0.1.0-preview.1.nupkg --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
dotnet nuget push artifacts/nuget/0.1.0-preview.1/CRules.0.1.0-preview.1.nupkg --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
```

先 Abstractions，后核心包。符号包置于相邻目录，按目标源的规则发布。上传后从目标源使用新缓存重新安装并核对依赖，本地 pack 成功不等于远端发布成功。包中的仓库提交也应已推送，确保源码链接可访问。

## 当前边界

这是预览版。自定义 Delegate 的适配缺陷仍在 [已知问题](%E5%B7%B2%E7%9F%A5%E9%97%AE%E9%A2%98.md) 中，稳定使用 Func 或 Type 入口。不宣称完整 C#、任意动态对象或零警告的库级 AOT。NativeAOT 使用文档中的 Interpreted 配置，不沿用 JIT 性能结论。
