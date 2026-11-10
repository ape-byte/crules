using BenchmarkDotNet.Attributes;

namespace CRules.Benchmarks;

/// <summary>固定预分配输入，剥离 context 构建；原生与规则采用相同展开算法。</summary>
[MemoryDiagnoser]
public class BusinessRuleHotBenchmarks
{
    [Params("S1", "S2", "S3", "S5")]
    public string Scenario { get; set; } = "S1";
    private BusinessContext _input = null!;
    private Func<BusinessContext, long> _compiled = null!;
    private Func<BusinessContext, long> _native = null!;
    [GlobalSetup]
    public void Setup()
    {
        var scenario = BusinessRules.Scenarios.Single(s => s.Id == Scenario);
        _input = BusinessContexts.Create(new Random(20261109), 42, scenario.Large);
        _compiled = BenchmarkCompiler.Compile<BusinessContext, long>(BusinessRules.Clr(scenario));
        _native = NativeBusinessEquivalent.Get(Scenario);
        long expected = BusinessRules.Reference(scenario, _input);
        if (_compiled(_input) != expected || _native(_input) != expected) throw new InvalidOperationException("Pure-rule results differ.");
    }
    [Benchmark(Baseline = true)] public long NativeEquivalent() => _native(_input);
    [Benchmark] public long CRulesHot() => _compiled(_input);
}
