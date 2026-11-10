namespace CRules.Tests;

public sealed class Syntax10Tests
{
    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public void Switch_statements_group_labels_break_and_nested_switches(RuleExecutionMode mode)
    {
        var rule = new RuleCompiler(new() { ExecutionMode = mode }).Compile<int, decimal>("""
            var value = 1m;
            switch (ctx) {
                case 1:
                case 2: value *= 0.8m; break;
                case 3:
                    switch(ctx) { case 3: value += 2m; break; default: break; }
                    break;
                default: return 10m;
            }
            return value;
            """).RequireExecutable();
        Assert.Equal(0.8m, rule.Execute(1));
        Assert.Equal(0.8m, rule.Execute(2));
        Assert.Equal(3m, rule.Execute(3));
        Assert.Equal(10m, rule.Execute(4));
    }

    [Theory]
    [InlineData("switch(ctx) { case 1: return 0.9m; default: return 1m; }")]
    [InlineData("return ctx switch { 1 => 0.9m, _ => 1m }; ")]
    [InlineData("var x = ctx switch { 1 => 0.9m, _ => 1m, }; return x;")]
    [InlineData("if(ctx==1) return 0.9m; else return 1m;")]
    public void Branch_forms_preserve_returns(string code)
    {
        var rule = new RuleCompiler().Compile<int, decimal>(code).RequireExecutable();
        Assert.Equal(0.9m, rule.Execute(1));
        Assert.Equal(1m, rule.Execute(2));
    }

    [Fact]
    public void Switch_selector_executes_once_and_nonselected_branches_are_lazy()
    {
        var input = new CountingInput();
        var rule = new RuleCompiler().Compile<CountingInput, decimal>(
            "return ctx.Next switch { 1 => 5m, _ => 1m / ctx.Zero }; ").RequireExecutable();
        Assert.Equal(5m, rule.Execute(input));
        Assert.Equal(1, input.Count);
        var statement = new RuleCompiler().Compile<CountingInput, decimal>(
            "switch(ctx.Next) { case 1: return 5m; default: return 1m / ctx.Zero; }").RequireExecutable();
        Assert.Equal(5m, statement.Execute(new CountingInput()));
    }

    [Fact]
    public void Switch_literals_enum_null_and_numeric_promotion()
    {
        var compiler = new RuleCompiler(new() { ReferencedTypes = [typeof(DayOfWeek)] });
        Assert.Equal(1, compiler.Compile<DayOfWeek, int>("return ctx switch { DayOfWeek.Monday => 1, _ => 2 };").RequireExecutable().Execute(DayOfWeek.Monday));
        var nullable = compiler.Compile<string, int>("return ctx switch { null => 0, \"a,b=>\" => 1, _ => 2 };").RequireExecutable();
        Assert.Equal(0, nullable.Execute(null!));
        Assert.Equal(1, nullable.Execute("a,b=>"));
        Assert.Equal(2m, compiler.Compile<int, decimal>("var result = ctx switch { 1 => 1, _ => 2m }; return result;").RequireExecutable().Execute(0));
    }

    [Fact]
    public void Switch_negative_literals_and_grouped_default_are_constants()
    {
        var compiler = new RuleCompiler();
        Assert.Equal(1, compiler.Compile<int, int>("switch(ctx){case -1:return 1;default:return 2;}").RequireExecutable().Execute(-1));
        Assert.Equal(1, compiler.Compile<int, int>("return ctx switch {-1=>1,_=>2};").RequireExecutable().Execute(-1));
        var grouped = compiler.Compile<int, int>("switch(ctx){default:case 1:return 3;case 2:return 4;}").RequireExecutable();
        Assert.Equal(3, grouped.Execute(1)); Assert.Equal(3, grouped.Execute(0)); Assert.Equal(4, grouped.Execute(2));
    }

    [Fact]
    public void Single_if_else_binds_to_nearest_if()
    {
        var rule = new RuleCompiler().Compile<int, int>("if(ctx>0) if(ctx==1) return 1; else return 2; return 0;").RequireExecutable();
        Assert.Equal(1, rule.Execute(1)); Assert.Equal(2, rule.Execute(3)); Assert.Equal(0, rule.Execute(-1));
    }

    [Theory]
    [InlineData("var x=10m; x+=2m; x-=1m; x*=3m; x/=11m; x%=2m; return x;", "1")]
    [InlineData("var x=\"a\"; x+=\"b\"; return x;", "ab")]
    public void Local_compound_assignment_uses_upstream_operator_rules(string code, string expected)
    {
        var rule = new RuleCompiler().Compile<Func<object>>(code, []).RequireExecutable();
        Assert.Equal(expected, Convert.ToString(rule.Execute(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Arrays_and_lists_are_materialized_return_values()
    {
        var compiler = new RuleCompiler(new() { ReferencedTypes = [typeof(List<>)] });
        Assert.Equal(new[] { 1, 2 }, compiler.Compile<Func<int[]>>("return new int[] { 1, 2 };", []).RequireExecutable().Execute());
        Assert.Equal(new[] { 1m, 2m }, compiler.Compile<Func<List<decimal>>>("return new List<decimal>() { 1m, 2m };", []).RequireExecutable().Execute());
    }

    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public void Conditional_break_paths_need_a_return_after_the_switch(RuleExecutionMode mode)
    {
        var compiler = new RuleCompiler(new() { ExecutionMode = mode });
        const string code = "switch(ctx){case 1:if(ctx>0) break;return 1m;default:return 2m;}";
        Assert.False(compiler.Compile<int, decimal>(code).Success);
        var rule = compiler.Compile<int, decimal>(code + "return 3m;").RequireExecutable();
        Assert.Equal(3m, rule.Execute(1));
        Assert.Equal(2m, rule.Execute(0));
        const string nested = "switch(ctx){case 1:switch(ctx){default:break;}return 4m;default:return 5m;}";
        Assert.Equal(4m, compiler.Compile<int, decimal>(nested).RequireExecutable().Execute(1));
    }

    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public void Nullable_labels_assignments_and_runtime_collection_returns(RuleExecutionMode mode)
    {
        var compiler = new RuleCompiler(new() { ExecutionMode = mode, ReferencedTypes = [typeof(List<>)] });
        var rule = compiler.Compile<int?, int>("var x=0;x=ctx switch {null=>3,1=>4,_=>5};return x;").RequireExecutable();
        Assert.Equal(3, rule.Execute(null)); Assert.Equal(4, rule.Execute(1)); Assert.Equal(5, rule.Execute(2));
        Assert.Equal(new[] { 1, 2 }, Assert.IsType<int[]>(compiler.Compile(new CompileOptions { Code = "return new int[] {1,2};", ReturnType = typeof(int[]) }).RequireExecutable().Execute()));
        Assert.Equal(new[] { 1m, 2m }, Assert.IsType<List<decimal>>(compiler.Compile(new CompileOptions { Code = "return new List<decimal>() {1m,2m};", ReturnType = typeof(List<decimal>) }).RequireExecutable().Execute()));
    }

    [Fact]
    public void Constant_label_member_policy_and_literal_content_are_preserved()
    {
        var compiler = new RuleCompiler(new() { ReferencedTypes = [typeof(DayOfWeek)], IsMemberAllowed = member => member.Name != "Monday" });
        Assert.False(compiler.Compile<DayOfWeek, int>("switch(ctx){case DayOfWeek.Monday:return 1;default:return 2;}").Success);
        Assert.False(compiler.Compile<DayOfWeek, int>("return ctx switch {DayOfWeek.Monday=>1,_=>2};").Success);
        Assert.Equal("++ 1e-15f", new RuleCompiler().Compile<Func<string>>("return \"++ 1e-15f\";", []).RequireExecutable().Execute());
        Assert.False(new RuleCompiler().Compile<int, int>("var x=ctx;return ++x;").Success);
    }

    [Theory]
    [InlineData("switch(ctx){case 1:return 1m;}")]
    [InlineData("switch(ctx){case 1:break;default:return 2m;}")]
    [InlineData("switch(ctx){case 1:var x=1m;default:return 2m;}return 3m;")]
    [InlineData("switch(ctx){case 1:return 1m;case 1:return 2m;default:return 3m;}")]
    [InlineData("switch(ctx){default:return 1m;default:return 2m;}")]
    [InlineData("break;return 1m;")]
    [InlineData("return ctx switch {1 => 1m};")]
    [InlineData("return ctx switch {_=>1m, 1=>2m};")]
    [InlineData("return ctx switch {1=>1m,1=>2m,_=>3m};")]
    [InlineData("return ctx switch {> 1=>1m,_=>2m};")]
    [InlineData("if(ctx>0) var x=1m;return 1m;")]
    [InlineData("ctx+=1;return 1m;")]
    [InlineData("const x=1m;x+=1m;return x;")]
    [InlineData("return [1,2];")]
    public void Unsupported_or_invalid_syntax_is_diagnostic(string code)
    {
        var result = new RuleCompiler().Compile<int, decimal>(code);
        Assert.False(result.Success);
        Assert.All(result.Diagnostics, d => Assert.InRange(d.Span.Start + d.Span.Length, 0, code.Length));
    }
}

public sealed class CountingInput
{
    public int Count { get; private set; }
    public int Next => ++Count;
    public decimal Zero => 0m;
}
