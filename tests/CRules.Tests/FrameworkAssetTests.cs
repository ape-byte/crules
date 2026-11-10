using System.Reflection;
using System.Runtime.Versioning;

namespace CRules.Tests;

public sealed class FrameworkAssetTests
{
    [Fact]
    public void Engine_and_contracts_use_the_requested_framework_assets()
    {
#if CRULES_TEST_NETSTANDARD
        const string expected = ".NETStandard,Version=v2.0";
#else
        string expected = typeof(FrameworkAssetTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
#endif
        Assert.Equal(expected, typeof(RuleCompiler).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
        Assert.Equal(expected, typeof(CompileOptions).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
#if NET6_0
        Assert.Equal(6, Environment.Version.Major);
#elif NET8_0
        Assert.Equal(8, Environment.Version.Major);
#elif NET10_0
        Assert.Equal(10, Environment.Version.Major);
#endif
    }
}
