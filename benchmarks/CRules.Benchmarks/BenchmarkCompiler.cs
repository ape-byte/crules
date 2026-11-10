#if CRULES_BASELINE
using Options = CRules.RuleCompileOptions;
#else
using Options = CRules.CompilerOptions;
#endif

namespace CRules.Benchmarks;

/// <summary>让同一套测试工具分别引用重构前后核心；仅适配 API，不包装执行热路径。</summary>
public static class BenchmarkCompiler
{
    private static int _recorded;
    public static RuleCompiler Create(bool decimalOnly = false, bool lambda = false)
    {
        RecordProvenance();
        return new(new Options { DecimalOnly = decimalOnly, AllowRuntimeLambdaBinding = lambda });
    }
    public static void RecordProvenance()
    {
        string? folder = Environment.GetEnvironmentVariable("CRULES_BENCH_PROVENANCE_DIR");
        if (folder is null || Interlocked.Exchange(ref _recorded, 1) != 0) return;
        Directory.CreateDirectory(folder);
        string core = typeof(RuleCompiler).Assembly.Location;
        string harness = typeof(BenchmarkCompiler).Assembly.Location;
        File.WriteAllText(Path.Combine(folder, Environment.ProcessId + ".json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            ProcessId = Environment.ProcessId, CorePath = core,
            CoreSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(core))),
            HarnessPath = harness, HarnessSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(harness)))
        }));
    }

    public static Func<TInput, TResult> Compile<TInput, TResult>(string code, bool decimalOnly = false, bool lambda = false)
        => Compile<TInput, TResult>(Create(decimalOnly, lambda), code);

    public static Func<TInput, TResult> Compile<TInput, TResult>(RuleCompiler compiler, string code)
    {
#if CRULES_BASELINE
        return compiler.Compile<TInput, TResult>(new RuleSource("benchmark", "1", code)).RequireRule().Execute;
#else
        return compiler.Compile<TInput, TResult>(code).RequireExecutable().Execute;
#endif
    }

    public static object CompileArtifact<TInput, TResult>(RuleCompiler compiler, string code)
    {
#if CRULES_BASELINE
        return compiler.Compile<TInput, TResult>(new RuleSource("benchmark", "1", code)).RequireRule();
#else
        return compiler.Compile<TInput, TResult>(code).RequireExecutable();
#endif
    }
}
