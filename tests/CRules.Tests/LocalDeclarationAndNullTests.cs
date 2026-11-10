namespace CRules.Tests;

public sealed class LocalDeclarationAndNullTests
{
    private static RuleCompiler Compiler(RuleExecutionMode mode) => new(new()
    {
        ExecutionMode = mode,
        ReferencedTypes = [typeof(List<>), typeof(Dictionary<,>), typeof(Decision), typeof(DayOfWeek)]
    });

    public static IEnumerable<object[]> PrimitiveDeclarations()
    {
        (string Code, object Expected)[] cases =
        [
            ("sbyte a = -100; return a;", (sbyte)-100),
            ("byte a = 100; return a;", (byte)100),
            ("short a = -1000; return a;", (short)-1000),
            ("ushort a = 1000; return a;", (ushort)1000),
            ("int a = 100; return a;", 100),
            ("uint a = 100U; return a;", 100U),
            ("long a = 100L; return a;", 100L),
            ("ulong a = 100UL; return a;", 100UL),
            ("float a = 1.25f; return a;", 1.25f),
            ("double a = 1.25d; return a;", 1.25d),
            ("decimal a = 100m; return a;", 100m),
            ("bool a = true; return a;", true),
            ("char a = '中'; return a;", '中'),
            ("string a = \"abc\"; return a;", "abc"),
            ("object a = 100; return a;", 100),
            ("int? a = 100; return a;", 100),
            ("long? a = null; a = 100L; return a;", 100L),
            ("float? a = 1.25f; return a;", 1.25f)
        ];
        return cases.SelectMany(c => Enum.GetValues<RuleExecutionMode>().Select(mode => new object[] { c.Code, c.Expected, mode }));
    }

    [Theory]
    [MemberData(nameof(PrimitiveDeclarations))]
    public void Builtin_types_can_be_declared_directly(string code, object expected, RuleExecutionMode mode)
        => Assert.Equal(expected, Compiler(mode).Compile<Func<object>>(code, []).RequireExecutable().Execute());

    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public void Explicit_locals_keep_the_declared_type_and_support_target_new(RuleExecutionMode mode)
    {
        var rule = Compiler(mode).Compile<Func<decimal, decimal, List<decimal>>>("""
            decimal total = amount;
            total *= rate;
            List<decimal> values = new() { total, amount, rate };
            values = new List<decimal>() { values[0], 2m };
            return values;
            """, ["amount", "rate"]).RequireExecutable();
        Assert.Equal(new[] { 90m, 2m }, rule.Execute(100m, 0.9m));
        Assert.Equal(new[] { 20m, 2m }, rule.Execute(40m, 0.5m));
        var decision = Compiler(mode).Compile<Func<Decision>>("""
            Decision result = new() { Amount = 3m };
            return result;
            """, []).RequireExecutable();
        Assert.Equal(3m, decision.Execute().Amount);
        Assert.NotSame(decision.Execute(), decision.Execute());
        Assert.Equal(4m, Compiler(mode).Compile<Func<decimal>>("decimal x = new(4); return x;", []).RequireExecutable().Execute());
    }

    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public void Type_forms_and_implicit_assignment_conversions_are_checked(RuleExecutionMode mode)
    {
        var compiler = Compiler(mode);
        Assert.Equal(100m, compiler.Compile<Func<decimal>>("decimal a = 100; return a;", []).RequireExecutable().Execute());
        Assert.Equal((byte)100, compiler.Compile<Func<byte>>("byte a = 100; return a;", []).RequireExecutable().Execute());
        Assert.Equal(new[] { 1m, 2m }, compiler.Compile<Func<decimal[]>>("decimal[] a = new[] { 1m, 2m }; return a;", []).RequireExecutable().Execute());
        Assert.Equal(DayOfWeek.Monday, compiler.Compile<Func<DayOfWeek>>("DayOfWeek a = DayOfWeek.Monday; return a;", []).RequireExecutable().Execute());
        Assert.Equal(2, compiler.Compile<Func<int>>("Dictionary<string, List<decimal>> a = new(); return a.Count + 2;", []).RequireExecutable().Execute());
        Assert.IsType<Decision>(compiler.Compile<Func<object>>("object a = new Decision(); return a;", []).RequireExecutable().Execute());
        Assert.Equal(3m, compiler.Compile<Func<decimal>>("decimal a = 1m; { decimal b = 2m; a += b; } return a;", []).RequireExecutable().Execute());
    }

    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public void List_shorthand_infers_elements_without_evaluating_them(RuleExecutionMode mode)
    {
        var compiler = Compiler(mode);
        var rule = compiler.Compile<Func<decimal, decimal, List<decimal>>>(
            "List a = new() { amount, rate }; return a;", ["amount", "rate"]).RequireExecutable();
        Assert.Equal(new[] { 100m, 0.9m }, rule.Execute(100m, 0.9m));
        Assert.Equal(new[] { 1m, 2m }, compiler.Compile<Func<List<decimal>>>("List a = new() { 1, 2m }; return a;", []).RequireExecutable().Execute());
        Assert.Equal(new string?[] { null, "a,b" }, compiler.Compile<Func<List<string?>>>("List a = new() { null, \"a,b\", }; return a;", []).RequireExecutable().Execute());
        Assert.Equal(new[] { 1m, 2m }, compiler.Compile<Func<List<decimal>>>("List a = new\t/* comment */() { 1m, 2m }; return a;", []).RequireExecutable().Execute());
        var input = new CountingInput();
        var sideEffects = compiler.Compile<CountingInput, List<int>>("List a = new() { ctx.Next, ctx.Next }; return a;").RequireExecutable();
        Assert.Equal(0, input.Count);
        Assert.Equal(new[] { 1, 2 }, sideEffects.Execute(input));
        Assert.Equal(2, input.Count);
    }

    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public async Task Null_returns_and_nullable_locals_work_with_both_execution_apis(RuleExecutionMode mode)
    {
        var compiler = Compiler(mode);
        foreach (string code in new[] { "return null;", "return (null);", "string a = null; return a;", "string? a = null; return a;" })
        {
            var rule = compiler.Compile<Func<string?>>(code, []).RequireExecutable();
            Assert.Null(rule.Execute());
            Assert.Null(((Executable)rule).Execute());
            Assert.Null(await rule.ExecuteAsync());
        }
        foreach (string code in new[] { "return null;", "decimal? a = null; return a;", "decimal? a = 1m; a = null; return a;" })
        {
            Assert.Null(compiler.Compile<Func<decimal?>>(code, []).RequireExecutable().Execute());
            Assert.Null(compiler.Compile(new CompileOptions { Code = code, ReturnType = typeof(decimal?) }).RequireExecutable().Execute());
        }
        Assert.Null(compiler.Compile<Func<object?>>("object a = null; return a;", []).RequireExecutable().Execute());
        Assert.Null(compiler.Compile<Func<List<decimal>?>>("List<decimal>? a = null; return a;", []).RequireExecutable().Execute());
        var optional = compiler.Compile<bool, Decision?>("if (ctx) { Decision? a = null; return a; } else { return new() { Amount = 2m }; }").RequireExecutable();
        Assert.Null(optional.Execute(true));
        Assert.Equal(2m, optional.Execute(false)!.Amount);
        Assert.Null(compiler.Compile<Decision?, Decision?>("return ctx;").RequireExecutable().Execute(null));
        Assert.Null(compiler.Compile<bool, Decision?>("return ctx ? null : new Decision();").RequireExecutable().Execute(true));
        Assert.Null(compiler.Compile<bool, Decision?>("return ctx switch { true => null, _ => new Decision() };").RequireExecutable().Execute(true));
        Assert.Null(compiler.Compile<Func<decimal?>>("decimal? a = new(); return a;", []).RequireExecutable().Execute());
        Assert.Null(compiler.Compile<Func<Decision>>("return null;", []).RequireExecutable().Execute());
        Assert.Null(compiler.Compile<Envelope, Decision?>("return ctx.Child;").RequireExecutable().Execute(new()));
        var fromHost = compiler.Compile<NullableResultHost, Task<Decision?>>("return ctx.GetAsync();").RequireExecutable();
        Assert.Null(await fromHost.Execute(new()));
        Assert.Null(await fromHost.ExecuteAsync(new NullableResultHost()));
        Assert.Null(await compiler.Compile(new CompileOptions
        {
            Code = "return ctx.GetValueAsync();",
            ReturnType = typeof(ValueTask<Decision?>),
            Parameters = [new("ctx", typeof(NullableResultHost))]
        }).RequireExecutable().ExecuteAsync(new NullableResultHost()));
    }

    [Theory]
    [InlineData("decimal a; return 1m;")]
    [InlineData("decimal a = 1m, b = 2m; return a;")]
    [InlineData("var a = 1m, b = 2m; return a;")]
    [InlineData("decimal a = \"100\"; return a;")]
    [InlineData("decimal a = null; return a;")]
    [InlineData("byte a = 256; return a;")]
    [InlineData("int a = 1.5; return a;")]
    [InlineData("float a = 1.25d; return a;")]
    [InlineData("int a = null; return a;")]
    [InlineData("bool a = null; return a;")]
    [InlineData("string?? a = null; return a;")]
    [InlineData("decimal? a = null; decimal b = a; return b;")]
    [InlineData("object a = null; Decision b = a; return b;")]
    [InlineData("decimal a = 1m; a = \"1\"; return a;")]
    [InlineData("Missing a = new(); return a;")]
    [InlineData("List a = new() {}; return a;")]
    [InlineData("List a = new() {null}; return a;")]
    [InlineData("List a = new() {1m, \"a\"}; return a;")]
    [InlineData("List a = new(); return a;")]
    [InlineData("List a = null; return a;")]
    [InlineData("var a = new(); return a;")]
    [InlineData("var a = null; return a;")]
    [InlineData("object a = new(); return a;")]
    [InlineData("List<decimal> a = new(\"invalid\"); return a;")]
    [InlineData("List<decimal> a = new() { \"invalid\" }; return a;")]
    [InlineData("Decision a = new() { Unknown = 1m }; return a;")]
    [InlineData("decimal ctx = 1m; return ctx;")]
    [InlineData("decimal a = 1m; { decimal a = 2m; } return a;")]
    [InlineData("if(ctx) decimal a = 1m; return 0m;")]
    public void Invalid_declarations_produce_source_diagnostics(string code)
    {
        var result = Compiler(RuleExecutionMode.JitCompiled).Compile<bool, object>(code);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, d =>
        {
            Assert.InRange(d.Span.Start, 0, code.Length);
            Assert.InRange(d.Span.Start + d.Span.Length, 0, code.Length);
            Assert.DoesNotContain("__crules_", d.Message);
        });
    }

    [Fact]
    public void Type_registration_limits_member_audit_and_nonnullable_returns_remain_enforced()
    {
        Assert.False(new RuleCompiler().Compile<Func<object>>("List a = new() { 1m }; return a;", []).Success);
        Assert.False(new RuleCompiler().Compile<Func<object>>("Decision a = new(); return a;", []).Success);
        Assert.False(new RuleCompiler(new() { DecimalOnly = true }).Compile<Func<object>>("double a = 1d; return a;", []).Success);
        Assert.False(new RuleCompiler(new() { MaxLocals = 1 }).Compile<Func<decimal>>("decimal a = 1m; decimal b = 2m; return b;", []).Success);
        var audited = new RuleCompiler(new() { ReferencedTypes = [typeof(Decision), typeof(List<>)], IsMemberAllowed = m => m.Name != ".ctor" });
        Assert.False(audited.Compile<Func<Decision>>("Decision a = new(); return a;", []).Success);
        Assert.False(audited.Compile<Func<List<decimal>>>("List a = new() {1m}; return a;", []).Success);
        foreach (var code in new[] { "return null;", "return (null);", "decimal? a = null; return a;" })
            Assert.False(Compiler(RuleExecutionMode.JitCompiled).Compile<Func<decimal>>(code, []).Success);
        Assert.False(Compiler(RuleExecutionMode.JitCompiled).Compile<bool, Decision?>("if(ctx) return null;").Success);
    }

    [Fact]
    public void Target_new_diagnostics_map_to_the_original_local_initializer()
    {
        const string code = "\nDecision value = new /* comment */ () { Unknown = 1m }; return value;";
        var error = Assert.Single(Compiler(RuleExecutionMode.JitCompiled).Compile<Func<Decision>>(code, []).Diagnostics);
        Assert.Equal(code.IndexOf("Unknown", StringComparison.Ordinal), error.Span.Start);
        Assert.Equal(2, error.Line);
        Assert.DoesNotContain("__crules_", error.Message);
    }

    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public void Nullable_numeric_returns_preserve_values_and_reject_implicit_unwrapping(RuleExecutionMode mode)
    {
        var compiler = Compiler(mode);
        var branch = compiler.Compile<bool, decimal?>("if(ctx) return null; else return 2m;").RequireExecutable();
        Assert.Null(branch.Execute(true));
        Assert.Equal(2m, branch.Execute(false));
        var selection = compiler.Compile<bool, decimal?>("return ctx switch { true => null, _ => 2m };").RequireExecutable();
        Assert.Null(selection.Execute(true));
        Assert.Equal(2m, selection.Execute(false));
        Assert.Null(compiler.Compile<decimal?, decimal?>("return ctx;").RequireExecutable().Execute(null));
        Assert.False(compiler.Compile<decimal?, decimal>("return ctx;").Success);
        Assert.False(compiler.Compile<bool, decimal>("if(ctx) return null; else return 2m;").Success);
        Assert.False(compiler.Compile<Func<decimal>>("return (object)null;", []).Success);
        Assert.False(compiler.Compile<Func<decimal>>("return new Decision();", []).Success);
    }
}

public sealed class NullableResultHost
{
    public Task<Decision?> GetAsync() => Task.FromResult<Decision?>(null);
    public ValueTask<Decision?> GetValueAsync() => ValueTask.FromResult<Decision?>(null);
}
