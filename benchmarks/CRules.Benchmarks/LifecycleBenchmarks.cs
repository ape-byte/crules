using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Jint;
using Acornima.Ast;

namespace CRules.Benchmarks;

[MemoryDiagnoser]
public class CompilationBenchmarks
{
    private readonly RuleCompiler _compiler = BenchmarkCompiler.Create();
    // AST 与编译委托产物不同，这组单独报告，不宣称是同等工作量。
    [Benchmark]
    public object ClrParseAuditCompile() => BenchmarkCompiler.CompileArtifact<RoutingInput, int>(_compiler, RoutingBenchmarks.ClrSource);
    [Benchmark] public Prepared<Script> JintPrepareAst() => Engine.PrepareScript(RoutingBenchmarks.JsSource);
    [Benchmark]
    public int JintPrepareCreateExecute()
    {
        var prepared = Engine.PrepareScript(RoutingBenchmarks.JsSource + "decide({tokens:750,premium:true});");
        using var engine = new Engine(); return (int)engine.Evaluate(prepared).AsNumber();
    }
}

[MemoryDiagnoser]
public class HostLifecycleBenchmarks : IDisposable
{
    private const string Json = "{\"Tokens\":750,\"Premium\":true}";
    private const string Wrapper = """
        (function(){ 'use strict'; const ctx=JSON.parse(__contextJson);
        Object.freeze(ctx);
        function decide(ctx){const score=ctx.Tokens*2;return ctx.Premium&&score>1000?2:1;}
        return JSON.stringify(decide(ctx)); })();
        """;
    private readonly Prepared<Script> _prepared = Engine.PrepareScript(Wrapper);
    private Func<RoutingInput, int> _compiled = null!;
    [GlobalSetup]
    public void Setup()
    {
        _compiled = BenchmarkCompiler.Compile<RoutingInput, int>(RoutingBenchmarks.ClrSource);
        if (ClrJsonSnapshot() != JintFreshEngineJson()) throw new InvalidOperationException("宿主结果不一致。");
    }
    [Benchmark(Baseline = true)]
    public string ClrJsonSnapshot()
        => JsonSerializer.Serialize(_compiled(JsonSerializer.Deserialize<RoutingInput>(Json)!));
    [Benchmark]
    public string JintFreshEngineJson()
    {
        using var engine = new Engine(o => o.MaxStatements(10000).TimeoutInterval(TimeSpan.FromMilliseconds(250)).LimitMemory(8000000).LimitRecursion(32));
        engine.SetValue("__contextJson", Json);
        return engine.Evaluate(_prepared).AsString();
    }
    [Benchmark]
    public Engine JintCreateAndDispose()
    {
        var engine = new Engine(); engine.Dispose(); return engine;
    }
    public void Dispose() { GC.SuppressFinalize(this); }
}
