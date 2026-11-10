namespace CRules.Tests;

public sealed class DictionaryTests
{
    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public async Task Signature_types_and_generic_arguments_are_visible_without_references(RuleExecutionMode mode)
    {
        var compiler = new RuleCompiler(new() { ExecutionMode = mode });
        var rule = compiler.Compile<Decision, Dictionary<string, List<Decision>>>("""
            Decision item = new() { Amount = ctx.Amount };
            Dictionary<string, List<Decision>> result = new() { ["items"] = new() { item } };
            return result;
            """).RequireExecutable();
        var first = rule.Execute(new() { Amount = 3m });
        Assert.Equal(3m, first["items"][0].Amount);
        Assert.Equal(4m, Assert.IsType<Dictionary<string, List<Decision>>>(await rule.ExecuteAsync(new Decision { Amount = 4m }))["items"][0].Amount);
        var runtime = compiler.Compile(new CompileOptions
        {
            Code = "Dictionary<string, Decision> d = new() { { \"one\", new Decision { Amount = ctx.Amount } } }; return d;",
            ReturnType = typeof(Dictionary<string, Decision>),
            Parameters = [new("ctx", typeof(Decision))]
        }).RequireExecutable();
        Assert.Equal(5m, Assert.IsType<Dictionary<string, Decision>>(runtime.Execute(new Decision { Amount = 5m }))["one"].Amount);
        Assert.Null(compiler.Compile<Func<Dictionary<string, Decision>?>>("return null;", []).RequireExecutable().Execute());
        Assert.Equal(2m, compiler.Compile<Func<Decision>>("return new Decision { Amount = 2m };", []).RequireExecutable().Execute().Amount);
        Assert.Equal(2m, compiler.Compile<Dictionary<string, decimal>, IDictionary<string, decimal>>("IDictionary<string, decimal> d = new Dictionary<string, decimal>(ctx); return d;").RequireExecutable().Execute(new() { ["a"] = 2m })["a"]);
        Assert.Equal(2m, compiler.Compile<Dictionary<string, decimal>, IReadOnlyDictionary<string, decimal>>("return ctx;").RequireExecutable().Execute(new() { ["a"] = 2m })["a"]);
        Assert.Equal(2m, compiler.Compile<Dictionary<string, Decision>, Dictionary<string, Decision>>("ctx[\"a\"] = new() { Amount = 2m }; return ctx;").RequireExecutable().Execute(new())["a"].Amount);
    }

    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public void Dictionary_methods_index_writes_and_out_values_work(RuleExecutionMode mode)
    {
        var compiler = new RuleCompiler(new() { ExecutionMode = mode });
        var rule = compiler.Compile<Dictionary<string, decimal>, decimal>("""
            Dictionary<string, decimal> copy = new(ctx);
            copy.Add("b", 2m);
            copy["a"] = 3m;
            decimal value = 0m;
            if (copy.TryGetValue("a", out value)) {
                copy.Remove("b");
                return value + copy.Count;
            }
            return -1m;
            """).RequireExecutable();
        var input = new Dictionary<string, decimal> { ["a"] = 1m };
        Assert.Equal(4m, rule.Execute(input));
        Assert.Equal(1m, input["a"]);
        Assert.Equal(2m, compiler.Compile<Dictionary<string, decimal>, decimal>("if(ctx.TryGetValue(\"a\", out var value)) return value; return 0m;").RequireExecutable().Execute(new() { ["a"] = 2m }));
        Assert.Equal(0m, compiler.Compile<Dictionary<string, decimal>, decimal>("if(ctx.TryGetValue(\"a\", out var value)) return value; return value;").RequireExecutable().Execute(new()));
        Assert.True(compiler.Compile<Dictionary<string, decimal>, bool>("ctx.Clear(); return ctx.Count == 0;").RequireExecutable().Execute(new() { ["a"] = 2m }));
        Assert.Equal(2m, compiler.Compile<IReadOnlyDictionary<string, decimal>, decimal>("if(ctx.TryGetValue(\"a\", out var value)) return value; return 0m;").RequireExecutable().Execute(new Dictionary<string, decimal> { ["a"] = 2m }));
    }

    [Theory]
    [InlineData("Add")]
    [InlineData("set_Item")]
    [InlineData("Item")]
    [InlineData(".ctor")]
    public void Both_initializer_forms_and_writes_respect_member_policy(string denied)
    {
        var compiler = new RuleCompiler(new() { IsMemberAllowed = m => m.Name != denied });
        string code = denied == "Add" ? "return new() { { \"a\", 1m } };" : "Dictionary<string, decimal> d = new(); d[\"a\"] = 1m; return d;";
        var result = compiler.Compile<Func<Dictionary<string, decimal>>>(code, []);
        Assert.False(result.Success);
        Assert.Equal("CRL4002", Assert.Single(result.Diagnostics).Code);
        if (denied == "set_Item")
            Assert.False(compiler.Compile<Func<Dictionary<string, decimal>>>("return new() { [\"a\"] = 1m };", []).Success);
    }

    [Theory]
    [InlineData("const d = ctx; d = ctx; return d.Count;")]
    [InlineData("decimal x = 0m; ctx.TryGetValue(\"a\", out ctx); return 0;")]
    [InlineData("const x = 0m; ctx.TryGetValue(\"a\", out x); return 0;")]
    [InlineData("int x = 0; ctx.TryGetValue(\"a\", out x); return 0;")]
    [InlineData("ctx[\"a\"] = \"bad\"; return 0;")]
    [InlineData("ctx.Add(\"a\", \"bad\"); return 0;")]
    public void Invalid_writes_are_diagnostics(string code)
    {
        var result = new RuleCompiler().Compile<Dictionary<string, decimal>, int>(code);
        Assert.False(result.Success);
        Assert.All(result.Diagnostics, d => Assert.DoesNotContain("__crules_", d.Message));
    }

    [Fact]
    public void Reference_registration_is_additive_scoped_and_detects_ambiguous_names()
    {
        var compiler = new RuleCompiler();
        Assert.Equal(2m, compiler.Compile<Func<Decision>>("Decision d = new() {Amount=2m}; return d;", []).RequireExecutable().Execute().Amount);
        Assert.False(compiler.Compile<Func<object>>("return new Decision();", []).Success); // 不污染下次编译。
        var array = compiler.Compile<Func<Decision[]>>("Decision d = new(); return new[] { d };", []).RequireExecutable().Execute();
        Assert.Single(array);
        Assert.False(compiler.Compile<Func<object>>("return new Decision();", []).Success);
        var extra = new RuleCompiler(new() { ReferencedTypes = [typeof(Decision)] });
        Assert.IsType<Decision>(extra.Compile<Func<object>>("return new Decision();", []).RequireExecutable().Execute());
        var ambiguous = new RuleCompiler(new() { ReferencedTypes = [typeof(CollisionA.Value), typeof(CollisionB.Value)] })
            .Compile<Func<int>>("return 1;", []);
        Assert.Equal("CRL2002", Assert.Single(ambiguous.Diagnostics).Code);
    }

    public sealed class CollisionA { public sealed class Value { } }
    public sealed class CollisionB { public sealed class Value { } }
}
