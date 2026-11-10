using Acornima.Ast;
using Jint;
using Jint.Native;
using System.Text.Json;

namespace CRules.Benchmarks;

public sealed class BusinessExecutor : IDisposable
{
    public static readonly string[] Modes = ["BuildOnly", "NativeReference", "NativeEquivalent", "CRules", "JintClr", "JintJson", "JintSingleton", "JintIsolatedJson"];
    private readonly BusinessScenario _scenario;
    private readonly string _mode;
    private readonly Func<BusinessContext, long>? _clr;
    private readonly Engine? _engine;
    private readonly JsValue? _function;
    private readonly JsValue[] _arguments = [JsValue.Undefined];
    private readonly Prepared<Script> _prepared;

    public BusinessExecutor(BusinessScenario scenario, string mode)
    {
        _scenario = scenario; _mode = mode;
        string script = BusinessRules.Javascript(scenario);
        if (mode == "CRules")
            _clr = BenchmarkCompiler.Compile<BusinessContext, long>(BusinessRules.Clr(scenario));
        else if (mode == "NativeEquivalent") _clr = NativeBusinessEquivalent.Get(scenario.Id);
        else if (mode is "JintClr" or "JintJson" or "JintSingleton")
        {
            _engine = new Engine();
            _engine.Execute(Engine.PrepareScript(script + "\nfunction fromJson(json) { return decide(JSON.parse(json)); }"));
            _function = _engine.GetValue(mode == "JintClr" ? "decide" : "fromJson");
            if (mode == "JintSingleton") _prepared = Engine.PrepareScript("decide(ctx)");
        }
        else if (mode == "JintIsolatedJson")
        {
            _prepared = Engine.PrepareScript("(function(){'use strict';const ctx=JSON.parse(__json);delete globalThis.__json;" +
                "function freeze(v){if(v!==null&&typeof v==='object'){for(const k of Object.keys(v))freeze(v[k]);Object.freeze(v);}return v;}freeze(ctx);\n" +
                script + "\nreturn decide(ctx);})();");
        }
        else if (mode is not ("BuildOnly" or "NativeReference")) throw new ArgumentOutOfRangeException(nameof(mode));
    }

    public long Execute(BusinessContext context)
    {
        if (_mode == "BuildOnly") return 0;
        if (_mode == "NativeReference") return BusinessRules.Reference(_scenario, context);
        if (_mode is "CRules" or "NativeEquivalent") return _clr!(context);
        if (_mode == "JintSingleton")
        {
            _engine!.SetValue("ctx", context);
            return Number(_engine.Evaluate(_prepared));
        }
        if (_mode == "JintIsolatedJson")
        {
            string json = JsonSerializer.Serialize(context);
            using var engine = new Engine(o => o.MaxStatements(10000).LimitMemory(8_000_000).LimitRecursion(32));
            engine.SetValue("__json", json);
            return Number(engine.Evaluate(_prepared));
        }
        _arguments[0] = _mode == "JintClr" ? JsValue.FromObject(_engine!, context) : JsonSerializer.Serialize(context);
        var result = _engine!.Call(_function!, _arguments);
        _arguments[0] = JsValue.Undefined;
        return Number(result);
    }

    private static long Number(JsValue result)
    {
        double value = result.AsNumber();
        long integer = checked((long)value);
        if (value != integer || Math.Abs(value) > 9_007_199_254_740_991d) throw new InvalidOperationException("结果不在精确安全整数范围。");
        return integer;
    }

    public void Dispose() => _engine?.Dispose();
}
