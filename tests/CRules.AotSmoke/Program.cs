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
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AotInput))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(decimal))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Math))]
    public static int Run()
    {
        Console.WriteLine($"DynamicCodeSupported={RuntimeFeature.IsDynamicCodeSupported}");
        int failures=0;
        void Check(string name,Action test)
        {
            try { test(); Console.WriteLine($"PASS {name}"); }
            catch(Exception error) { failures++; Console.WriteLine($"FAIL {name}: {error}"); }
        }
        var compiler=new RuleCompiler(new(){DecimalOnly=true,ExecutionMode=RuleExecutionMode.Interpreted});
        const string source="var total=ctx.Amount*0.8m;if(ctx.Vip){total=total*0.9m;}return Math.Round(total,2,MidpointRounding.ToEven);";
        CompiledRule<AotInput,decimal>? baseline=null;
        Check("decimal_branch",()=> {
            baseline=compiler.Compile<AotInput,decimal>(new("aot","1",source)).RequireRule();
            if(baseline.ExecutionMode!=RuleExecutionMode.Interpreted||baseline.Execute(new(100m,true))!=72m) throw new Exception("value/backend mismatch");
        });
        Check("repeated_inputs",()=> { if(baseline!.Execute(new(100m,false))!=80m) throw new Exception("state leaked"); });
        Check("scope_and_assignment",()=>Expect(compiler,"var x=1m;{var y=ctx.Amount;x=x+y;}return x;",101m));
        Check("const_binding",()=>Expect(compiler,"const x=ctx.Amount;return x;",100m));
        Check("early_return",()=>Expect(compiler,"if(ctx.Vip){return 7m;}return 9m;",7m));
        Check("positive_round",()=>Expect(compiler,"return Math.Round(1.005m,2,MidpointRounding.ToEven);",1m));
        Check("negative_round",()=>Expect(compiler,"return Math.Round(-1.005m,2,MidpointRounding.AwayFromZero);",-1.01m));
        Check("bool_result",()=> {
            var rule=compiler.Compile<AotInput,bool>(new("bool","1","return ctx.Vip && ctx.Amount > 0m;")).RequireRule();
            if(!rule.Execute(new(100m,true))) throw new Exception("bool failed");
        });
        Check("compile_diagnostic",()=> {
            var result=compiler.Compile<AotInput,decimal>(new("bad","1","return missing;"));
            if(result.Success||result.Diagnostics[0].Code!="CRL3001") throw new Exception("diagnostic failed");
        });
        Check("jit_guard",()=> {
            var result=new RuleCompiler().Compile<AotInput,decimal>(new("jit","1","return 1m;"));
            if(!RuntimeFeature.IsDynamicCodeSupported&&(result.Success||result.Diagnostics[0].Code!="CRL4004")) throw new Exception("silent fallback");
        });
        Check("divide_by_zero",()=> {
            var rule=compiler.Compile<AotInput,decimal>(new("zero","1","return ctx.Amount / (ctx.Amount - ctx.Amount);")).RequireRule();
            try { rule.Execute(new(100m,true));throw new Exception("division did not fail"); } catch(DivideByZeroException) { }
        });
        Check("overflow",()=> {
            var rule=compiler.Compile<AotInput,decimal>(new("overflow","1","return decimal.MaxValue * ctx.Amount;")).RequireRule();
            try { rule.Execute(new(100m,true));throw new Exception("overflow did not fail"); } catch(OverflowException) { }
        });
        Check("unsupported_lambda_rejected",()=> {
            var result=compiler.Compile<AotInput,decimal>(new("lambda","1","return ctx.Items.Where(x => x > 2m).Sum();"));
            if(result.Success || result.Diagnostics[0].Code is not ("CRL3001" or "CRL4003")) throw new Exception("lambda must be rejected");
        });
        // 实验特性单独探测；不将失败冒充标准 AOT 支持。
        try {
            var experimental=new RuleCompiler(new(){ExecutionMode=RuleExecutionMode.Interpreted,AllowRuntimeLambdaBinding=true});
            var rule=experimental.Compile<AotInput,decimal>(new("linq","1","return ctx.Items.Where(x => x > 2m).Sum();")).RequireRule();
            Console.WriteLine($"EXPERIMENTAL_LINQ_RESULT={rule.Execute(new(100m,true))}");
        } catch(Exception error) { Console.WriteLine($"EXPERIMENTAL_LINQ_UNSUPPORTED={error.GetType().Name}: {error.Message}"); }
        if(baseline is not null) {
            var input=new AotInput(100m,true);
            Profile("Interpreter",()=>baseline.Execute(input));
            Profile("NativeDelegate",()=>Native(input));
        }
        Console.WriteLine(failures==0?"AOT_SMOKE_PASS":"AOT_SMOKE_FAIL");
        return failures==0?0:1;
    }
    private static void Expect(RuleCompiler compiler,string source,decimal value) {
        var rule=compiler.Compile<AotInput,decimal>(new("case","1",source)).RequireRule();
        if(rule.Execute(new(100m,true))!=value) throw new Exception("value mismatch");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static decimal Native(AotInput input) {
        var total=input.Amount*0.8m;if(input.Vip)total*=0.9m;return Math.Round(total,2,MidpointRounding.ToEven);
    }
    private static void Profile(string name,Func<decimal> execute) {
        for(int i=0;i<10000;i++)execute();
        const int count=100000;decimal checksum=0m;
        long before=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
        for(int i=0;i<count;i++)checksum+=execute();
        watch.Stop();long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        if(checksum!=7200000m)throw new Exception("checksum mismatch");
        Console.WriteLine($"PROFILE {name}: ns/op={watch.Elapsed.TotalNanoseconds/count:F2}; B/op={bytes/(double)count:F3}; operations={count}");
    }
}
