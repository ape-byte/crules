CRules 公开 API 最小验证
验证日期：2026-11-09
依赖：DynamicExpresso.Core 2.19.6
环境：Windows，.NET SDK 10.0.401，.NET Runtime 10.0.12，Release
执行：dotnet run --project probe.csproj -c Release
预期：P01 至 P10 均 PASS。P06、P10 为预期拒绝测试。
说明：未注册 MidpointRounding 的初次试验失败；显式注册后 P09 通过。
本工程仅为计划书的技术证据，不是完整 CRules 引擎。
