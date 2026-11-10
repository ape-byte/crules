using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CRules;

return Smoke.Run();

public sealed record AotInput(decimal Amount, bool Vip)
{
    public decimal[] Items { get; init; } = [1m, 3m, 4m];
}

internal static class Smoke
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Dictionary<string, decimal>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Dictionary<string, AotDecision>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<decimal>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AotDecision))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(decimal?))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AotAsyncHost))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DataObject))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AotInput))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(decimal))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Math))]
    public static int Run()
    {
        Console.WriteLine($"DynamicCodeSupported={RuntimeFeature.IsDynamicCodeSupported}");
        GC.KeepAlive(Activator.CreateInstance<List<decimal>>());
        int failures = 0;
        void Check(string name, Action test)
        {
            try { test(); Console.WriteLine($"PASS {name}"); }
            catch (Exception error) { failures++; Console.WriteLine($"FAIL {name}: {error}"); }
        }
        var compiler = new RuleCompiler(new() { DecimalOnly = true, ExecutionMode = RuleExecutionMode.Interpreted });
        const string source = "var total=ctx.Amount*0.8m;if(ctx.Vip){total=total*0.9m;}return Math.Round(total,2,MidpointRounding.ToEven);";
        Executable<Func<AotInput, decimal>>? baseline = null;
        Check("decimal_branch", () =>
        {
            baseline = compiler.Compile<AotInput, decimal>(source).RequireExecutable();
            if (baseline.ExecutionMode != RuleExecutionMode.Interpreted || baseline.Execute(new(100m, true)) != 72m) throw new Exception("value/backend mismatch");
        });
        Check("repeated_inputs", () => { if (baseline!.Execute(new(100m, false)) != 80m) throw new Exception("state leaked"); });
        Check("scope_and_assignment", () => Expect(compiler, "var x=1m;{var y=ctx.Amount;x=x+y;}return x;", 101m));
        Check("const_binding", () => Expect(compiler, "const x=ctx.Amount;return x;", 100m));
        Check("early_return", () => Expect(compiler, "if(ctx.Vip){return 7m;}return 9m;", 7m));
        Check("positive_round", () => Expect(compiler, "return Math.Round(1.005m,2,MidpointRounding.ToEven);", 1m));
        Check("negative_round", () => Expect(compiler, "return Math.Round(-1.005m,2,MidpointRounding.AwayFromZero);", -1.01m));
        Check("bool_result", () =>
        {
            var rule = compiler.Compile<AotInput, bool>("return ctx.Vip && ctx.Amount > 0m;").RequireExecutable();
            if (!rule.Execute(new(100m, true))) throw new Exception("bool failed");
        });
        Check("compile_diagnostic", () =>
        {
            var result = compiler.Compile<AotInput, decimal>("return missing;");
            if (result.Success || result.Diagnostics[0].Code != "CRL3001") throw new Exception("diagnostic failed");
        });
        Check("jit_guard", () =>
        {
            var result = new RuleCompiler().Compile<AotInput, decimal>("return 1m;");
            if (!RuntimeFeature.IsDynamicCodeSupported && (result.Success || result.Diagnostics[0].Code != "CRL4004")) throw new Exception("silent fallback");
        });
        Check("divide_by_zero", () =>
        {
            var rule = compiler.Compile<AotInput, decimal>("return ctx.Amount / (ctx.Amount - ctx.Amount);").RequireExecutable();
            try { rule.Execute(new(100m, true)); throw new Exception("division did not fail"); } catch (DivideByZeroException) { }
        });
        Check("overflow", () =>
        {
            var rule = compiler.Compile<AotInput, decimal>("return decimal.MaxValue * ctx.Amount;").RequireExecutable();
            try { rule.Execute(new(100m, true)); throw new Exception("overflow did not fail"); } catch (OverflowException) { }
        });
        Check("unsupported_lambda_rejected", () =>
        {
            var result = compiler.Compile<AotInput, decimal>("return ctx.Items.Where(x => x > 2m).Sum();");
            if (result.Success || result.Diagnostics[0].Code is not ("CRL3001" or "CRL4003")) throw new Exception("lambda must be rejected");
        });
        Check("named_multiple_inputs", () =>
        {
            var r = compiler.Compile<Func<decimal, decimal, decimal>>("decimal value = amount * rate; return value;", ["amount", "rate"]).RequireExecutable();
            if (r.Execute(100m, 0.9m) != 90m) throw new Exception("multiple inputs");
        });
        Check("dictionary_initializers_and_methods", () =>
        {
            var r = compiler.Compile<Func<decimal, Dictionary<string, decimal>>>("""
                var source = new Dictionary<string, decimal>() { { "amount", amount } };
                Dictionary<string, decimal> d = new(source) { ["rate"] = 0.9m };
                d["amount"] *= d["rate"];
                d.Add("extra", 1m);
                if(d.TryGetValue("amount", out var value)) { d["result"] = value; }
                d.Remove("extra");
                return d;
                """, ["amount"]).RequireExecutable();
            var d = r.Execute(100m);
            if(d["result"] != 90m || d.ContainsKey("extra")) throw new Exception("dictionary value");
        });
        Check("object_auto_reference_and_null", () =>
        {
            var r = compiler.Compile<bool, AotDecision?>("if(ctx) return null; AotDecision d = new() { Amount = 2m }; return d;").RequireExecutable();
            if(r.Execute(true) is not null || r.Execute(false)?.Amount != 2m) throw new Exception("object/null");
        });
        Check("nullable_value", () =>
        {
            var r = compiler.Compile<bool, decimal?>("decimal? v = null; if(ctx) { v = 2m; } return v;").RequireExecutable();
            if(r.Execute(false) is not null || r.Execute(true) != 2m) throw new Exception("nullable");
        });
        Check("runtime_type_entry", () =>
        {
            var r = compiler.Compile(new CompileOptions { Code = "return amount * rate;", ReturnType = typeof(decimal), Parameters = [new("amount", typeof(decimal)), new("rate", typeof(decimal))] }).RequireExecutable();
            if(!Equals(r.Execute(100m,0.9m),90m) || !Equals(r.ExecuteAsync(100m,0.9m).GetAwaiter().GetResult(),90m)) throw new Exception("runtime entry");
        });
        Check("switch_and_list", () =>
        {
            var r = compiler.Compile<int, List<decimal>>("decimal value = ctx switch { 1 => 2m, _ => 3m }; List<decimal> d = new() {value}; return d;").RequireExecutable();
            if(r.Execute(1)[0] != 2m || r.Execute(0)[0] != 3m) throw new Exception("switch/list");
        });
        Check("async_results", () =>
        {
            var task = compiler.Compile<AotAsyncHost, Task<decimal?>>("return ctx.TaskResult();").RequireExecutable();
            if(!Equals(task.ExecuteAsync(new AotAsyncHost()).GetAwaiter().GetResult(), 2m)) throw new Exception("Task result");
            var valueTask = compiler.Compile<AotAsyncHost, ValueTask<decimal?>>("return ctx.ValueResult();").RequireExecutable();
            if(valueTask.ExecuteAsync(new AotAsyncHost()).GetAwaiter().GetResult() is not null) throw new Exception("ValueTask null");
            var failed = compiler.Compile<AotAsyncHost, Task<decimal?>>("return ctx.Failed();").RequireExecutable();
            try { failed.ExecuteAsync(new AotAsyncHost()).GetAwaiter().GetResult(); throw new Exception("fault swallowed"); } catch (ArithmeticException) { }
        });
        Check("typed_json_data", () =>
        {
            var rule = compiler.Compile<DataObject, decimal>("return ctx.RequireDecimal(\"rate\") * 100m;").RequireExecutable();
            if(rule.Execute(DataObject.FromJson("{\"rate\":0.9}")) != 90m) throw new Exception("JSON data");
        });
        try
        {
            var dynamicCompiler = new RuleCompiler(new() { ExecutionMode = RuleExecutionMode.Interpreted });
            var rule = dynamicCompiler.Compile<System.Dynamic.ExpandoObject, decimal>("return (decimal)ctx.Amount;").RequireExecutable();
            IDictionary<string, object?> data = new System.Dynamic.ExpandoObject(); data["Amount"] = 2m;
            Console.WriteLine($"CAPABILITY_EXPANDO_RESULT={rule.Execute((System.Dynamic.ExpandoObject)data)}");
        }
        catch (Exception error) { Console.WriteLine($"CAPABILITY_EXPANDO_UNSUPPORTED={error.GetType().Name}: {error.Message}"); }
        // 实验特性单独探测；不将失败冒充标准 AOT 支持。
        try
        {
            var experimental = new RuleCompiler(new() { ExecutionMode = RuleExecutionMode.Interpreted, AllowRuntimeLambdaBinding = true });
            var rule = experimental.Compile<AotInput, decimal>("return ctx.Items.Where(x => x > 2m).Sum();").RequireExecutable();
            Console.WriteLine($"EXPERIMENTAL_LINQ_RESULT={rule.Execute(new(100m, true))}");
        }
        catch (Exception error) { Console.WriteLine($"EXPERIMENTAL_LINQ_UNSUPPORTED={error.GetType().Name}: {error.Message}"); }
        if (baseline is not null)
        {
            var input = new AotInput(100m, true);
            Profile("Interpreter", () => baseline.Execute(input));
            Profile("NativeDelegate", () => Native(input));
        }
        Console.WriteLine(failures == 0 ? "AOT_SMOKE_PASS" : "AOT_SMOKE_FAIL");
        return failures == 0 ? 0 : 1;
    }
    private static void Expect(RuleCompiler compiler, string source, decimal value)
    {
        var rule = compiler.Compile<AotInput, decimal>(source).RequireExecutable();
        if (rule.Execute(new(100m, true)) != value) throw new Exception("value mismatch");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static decimal Native(AotInput input)
    {
        var total = input.Amount * 0.8m; if (input.Vip) total *= 0.9m; return Math.Round(total, 2, MidpointRounding.ToEven);
    }
    private static void Profile(string name, Func<decimal> execute)
    {
        for (int i = 0; i < 10000; i++) execute();
        const int count = 100000; decimal checksum = 0m;
        long before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
        for (int i = 0; i < count; i++) checksum += execute();
        watch.Stop(); long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (checksum != 7200000m) throw new Exception("checksum mismatch");
        Console.WriteLine($"PROFILE {name}: ns/op={watch.Elapsed.TotalNanoseconds / count:F2}; B/op={bytes / (double)count:F3}; operations={count}");
    }
}

public sealed class AotDecision { public decimal Amount { get; set; } }
public sealed class AotAsyncHost
{
    public Task<decimal?> TaskResult() => Task.FromResult<decimal?>(2m);
    public ValueTask<decimal?> ValueResult() => ValueTask.FromResult<decimal?>(null);
    public Task<decimal?> Failed() => Task.FromException<decimal?>(new ArithmeticException("expected"));
}
