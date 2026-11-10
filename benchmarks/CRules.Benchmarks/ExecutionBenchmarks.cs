using System.Globalization;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Jint;
using Jint.Native;

namespace CRules.Benchmarks;

public sealed record RoutingInput(long Tokens, bool Premium);

[MemoryDiagnoser]
public class RoutingBenchmarks : IDisposable
{
    public const string ClrSource = "var score = ctx.Tokens * 2; if (ctx.Premium && score > 1000) { return 2; } return 1;";
    public const string JsSource = "function decide(ctx) { const score=ctx.tokens*2; if(ctx.premium && score>1000) return 2; return 1; }";
    private readonly RoutingInput _input = new(750, true);
    private readonly Engine _engine = new();
    private Func<RoutingInput, int> _compiled = null!;
    private readonly Func<RoutingInput, int> _nativeDelegate = Native;
    private JsValue _function = null!;
    private JsValue[] _arguments = null!;
    [GlobalSetup]
    public void Setup()
    {
        _compiled = BenchmarkCompiler.Compile<RoutingInput, int>(ClrSource);
        _engine.Execute(Engine.PrepareScript(JsSource + "\nconst input={tokens:750,premium:true};"));
        _function = _engine.GetValue("decide"); _arguments = [_engine.Evaluate("input")];
        if (NativeDirect() != CRulesHot() || NativeDirect() != JintHot()) throw new InvalidOperationException("路由结果不一致。");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Native(RoutingInput ctx)
    { long score = ctx.Tokens * 2; return ctx.Premium && score > 1000 ? 2 : 1; }
    [Benchmark(Baseline = true)] public int NativeDirect() => Native(_input);
    [Benchmark] public int NativeDelegate() => _nativeDelegate(_input);
    [Benchmark] public int CRulesHot() => _compiled(_input);
    // 预分配 JS 对象及参数数组，计时内不 Parse，不经 CLR 反射包装上下文。
    [Benchmark] public int JintHot() => (int)_engine.Call(_function, _arguments).AsNumber();
    public void Dispose() { _engine.Dispose(); GC.SuppressFinalize(this); }
}

public sealed record AccountingInput(long InputTokens, long OutputTokens, decimal InputPrice, decimal OutputPrice, decimal Factor);

[MemoryDiagnoser]
public class AccountingBenchmarks : IDisposable
{
    public const string ClrSource = "var input = (decimal)ctx.InputTokens * ctx.InputPrice; var output = (decimal)ctx.OutputTokens * ctx.OutputPrice; return Math.Round((input + output) / 1000000m * ctx.Factor, 6, MidpointRounding.ToEven);";
    private const string JsSource = """
        function decide(ctx) {
          const input=mul(ctx.inputTokens,ctx.inputPrice);
          const output=mul(ctx.outputTokens,ctx.outputPrice);
          return round(mul(div(add(input,output),'1000000'),ctx.factor));
        }
        const input={inputTokens:'12345',outputTokens:'6789',inputPrice:'1.25',outputPrice:'3.75',factor:'1.1'};
        """;
    private readonly AccountingInput _input = new(12345, 6789, 1.25m, 3.75m, 1.1m);
    private readonly Engine _engine = new();
    private Func<AccountingInput, decimal> _compiled = null!;
    private readonly Func<AccountingInput, decimal> _nativeDelegate = Native;
    private JsValue _function = null!;
    private JsValue[] _arguments = null!;
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    private static string S(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    [GlobalSetup]
    public void Setup()
    {
        _compiled = BenchmarkCompiler.Compile<AccountingInput, decimal>(ClrSource, decimalOnly: true);
        // decimal 结果以字符串穿过 JS 边界；互操作成本包含在此组中。
        _engine.SetValue("mul", (Func<string, string, string>)((a, b) => S(D(a) * D(b))));
        _engine.SetValue("add", (Func<string, string, string>)((a, b) => S(D(a) + D(b))));
        _engine.SetValue("div", (Func<string, string, string>)((a, b) => S(D(a) / D(b))));
        _engine.SetValue("round", (Func<string, string>)(a => S(Math.Round(D(a), 6, MidpointRounding.ToEven))));
        _engine.Execute(Engine.PrepareScript(JsSource));
        _function = _engine.GetValue("decide"); _arguments = [_engine.Evaluate("input")];
        if (NativeDirect() != CRulesHot() || NativeDirect() != JintDecimalHelpers()) throw new InvalidOperationException("会计结果不一致。");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static decimal Native(AccountingInput ctx) => Math.Round(((decimal)ctx.InputTokens * ctx.InputPrice + (decimal)ctx.OutputTokens * ctx.OutputPrice) / 1000000m * ctx.Factor, 6, MidpointRounding.ToEven);
    [Benchmark(Baseline = true)] public decimal NativeDirect() => Native(_input);
    [Benchmark] public decimal NativeDelegate() => _nativeDelegate(_input);
    [Benchmark] public decimal CRulesHot() => _compiled(_input);
    [Benchmark] public decimal JintDecimalHelpers() => D(_engine.Call(_function, _arguments).AsString());
    public void Dispose() { _engine.Dispose(); GC.SuppressFinalize(this); }
}

public sealed record CollectionInput(long[] Items, long Threshold);

[MemoryDiagnoser]
public class CollectionBenchmarks : IDisposable
{
    [Params(10, 100, 1000)] public int Count { get; set; }
    private CollectionInput _input = null!;
    private readonly Engine _engine = new();
    private Func<CollectionInput, long> _compiled = null!;
    private JsValue _function = null!;
    private JsValue[] _arguments = null!;
    [GlobalSetup]
    public void Setup()
    {
        _input = new(Enumerable.Range(0, Count).Select(i => (long)i).ToArray(), Count / 2);
        _compiled = BenchmarkCompiler.Compile<CollectionInput, long>("var t = ctx.Threshold; return ctx.Items.Where(x => x > t).Sum();", lambda: true);
        _engine.Execute(Engine.PrepareScript($"function decide(ctx){{return ctx.items.filter(x=>x>ctx.threshold).reduce((a,b)=>a+b,0);}} const input={{items:[{string.Join(',', _input.Items)}],threshold:{_input.Threshold}}};"));
        _function = _engine.GetValue("decide"); _arguments = [_engine.Evaluate("input")];
        if (NativeLinq() != CRulesHot() || NativeLinq() != JintHot()) throw new InvalidOperationException("集合结果不一致。");
    }
    [Benchmark(Baseline = true)] public long NativeLinq() { var t = _input.Threshold; return _input.Items.Where(x => x > t).Sum(); }
    [Benchmark] public long CRulesHot() => _compiled(_input);
    [Benchmark] public long JintHot() => (long)_engine.Call(_function, _arguments).AsNumber();
    public void Dispose() { _engine.Dispose(); GC.SuppressFinalize(this); }
}
