#if !CRULES_BASELINE
using BenchmarkDotNet.Attributes;

namespace CRules.Benchmarks;

[MemoryDiagnoser]
public class InvocationApiBenchmarks
{
    private readonly RoutingInput _input = new(750, true);
    private Executable<Func<RoutingInput, int>> _typed = null!;
    private Executable _runtime = null!;
    private Executable<Func<decimal, decimal, decimal>> _multi = null!;
    private Executable _multiRuntime = null!;
    [GlobalSetup]
    public void Setup()
    {
        BenchmarkCompiler.RecordProvenance();
        var compiler = new RuleCompiler();
        _typed = compiler.Compile<RoutingInput, int>(RoutingBenchmarks.ClrSource).RequireExecutable();
        _runtime = compiler.Compile(new CompileOptions { Code = RoutingBenchmarks.ClrSource, ReturnType = typeof(int), Parameters = [new("ctx", typeof(RoutingInput))] }).RequireExecutable();
        _multi = compiler.Compile<Func<decimal, decimal, decimal>>("return amount * rate;", ["amount", "rate"]).RequireExecutable();
        _multiRuntime = compiler.Compile(new CompileOptions { Code = "return amount * rate;", ReturnType = typeof(decimal), Parameters = [new("amount", typeof(decimal)), new("rate", typeof(decimal))] }).RequireExecutable();
        if (_typed.Execute(_input) != (int)_runtime.Execute(_input)! || _multi.Execute(100m, .9m) != (decimal)_multiRuntime.Execute(100m, .9m)!) throw new InvalidOperationException("API results differ.");
    }
    [Benchmark] public int TypedContext() => _typed.Execute(_input);
    [Benchmark] public object? RuntimeTypeContext() => _runtime.Execute(_input);
    [Benchmark] public decimal TypedNamedInputs() => _multi.Execute(100m, .9m);
    [Benchmark] public object? RuntimeTypeNamedInputs() => _multiRuntime.Execute(100m, .9m);
}
#endif
