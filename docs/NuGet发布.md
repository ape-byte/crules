# NuGet 构建与发布

仓库当前配置为 **0.1.0-preview.1**，目标框架为 **netstandard2.0 / net6.0 / net8.0 / net10.0**。这是本地发布准备，不代表已经上传到 nuget.org 或创建正式版。

## 一个对外包

只发布 **CRules**。包内包含 CRules.dll 和 CRules.Abstractions.dll，保留代码分层，不需要单独发布/安装 Abstractions 包。各目标依赖 DynamicExpresso.Core 2.19.6；netstandard2.0/net6.0 另依赖 System.Text.Json 8.0.6，现代目标使用共享框架 JSON。Jint、Roslyn、测试、基准程序和本地 tools 不打入生产包。

作者 Authors 和 Company 均为 **ape-byte**，项目/仓库地址为 https://github.com/ape-byte/crules。图标采用项目正式灰橙 PNG，并嵌入包内 icon.png。NuGet 页面实际所有者由发布账号及 nuget.org 权限决定，不能仅靠 Authors 字段设置。

元数据位于 [NuGet.props](../NuGet.props)。包带 README、LICENSE、图标、每组目标的两个程序集及 XML 文档；一个 snupkg 保存对应符号。Abstractions 项目设置 IsPackable=false，其构建产物由核心包纳入。

## 构建

在仓库根目录使用满足 global.json 的 SDK：

```powershell
dotnet restore CRules.slnx --locked-mode
dotnet test tests/CRules.Tests -c Release -f net10.0 --no-restore
dotnet pack src/CRules/CRules.csproj -c Release -o publish/packages
```

输出一个 CRules nupkg 和一个 snupkg（符号包，不是第二个运行依赖包）。`publish/` 已忽略；不指定 -o 时也默认输出到项目根目录 publish/packages。默认版本在 NuGet.props；临时覆盖版本时使用 `-p:Version=...`；两个内部程序集使用同一版本配置。

## 发布前验证

1. 检查 ID、版本、四组目标框架、MIT、README、图标和仓库提交。
2. 检查一个包中同时包含两个程序集，依赖按目标分组，包含 DynamicExpresso 和旧目标的 JSON，不依赖独立 Abstractions 包。
3. 新建独立的各目标消费项目，只用 PackageReference，从本地目录安装，不使用 ProjectReference。
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
dotnet nuget push publish/packages/CRules.0.1.0-preview.1.nupkg --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
```

只上传 CRules。符号包置于相邻目录，按目标源的规则发布。上传后从目标源使用新缓存重新安装并核对依赖，本地 pack 成功不等于远端发布成功。包中的仓库提交也应已推送，确保源码链接可访问。

## 当前边界

这是预览版。自定义 Delegate 的适配缺陷仍在 [已知问题](%E5%B7%B2%E7%9F%A5%E9%97%AE%E9%A2%98.md) 中，稳定使用 Func 或 Type 入口。不宣称完整 C#、任意动态对象或零警告的库级 AOT。NativeAOT 使用文档中的 Interpreted 配置，不沿用 JIT 性能结论。

完整框架选择、依赖分组和跨框架回归命令见 [框架兼容性](%E6%A1%86%E6%9E%B6%E5%85%BC%E5%AE%B9%E6%80%A7.md)。
