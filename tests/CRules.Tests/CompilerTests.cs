using System.Collections.Concurrent;
using System.Dynamic;
using System.Reflection;
using CRules;

namespace CRules.Tests;

public sealed record Input(decimal Amount = 100m, bool Vip = true, decimal[]? Values = null)
{
    public decimal[] Items => Values ?? [];
}

public sealed class CompilerTests
{
    private static RuleCompilation<Input, T> Compile<T>(string text, RuleCompileOptions? options = null)
        => new RuleCompiler(options).Compile<Input, T>(new("test", "1", text));

    [Fact]
    public void Locals_branches_blocks_and_early_returns_are_one_typed_delegate()
    {
        var rule = Compile<decimal>("""
            var total = ctx.Amount * 0.8m;
            if (ctx.Vip) { total = total * 0.9m; }
            if (total < 1m) { return 1m; } else { return total; }
            """).RequireRule();
        Assert.Equal(72m, rule.Execute(new()));
        Assert.Equal(80m, rule.Execute(new(Vip: false)));
        Assert.Equal(1m, rule.Execute(new(0)));
    }

    [Fact]
    public void Nested_lambda_captures_mutated_local_and_parameter_identity()
    {
        var rule = Compile<decimal>("""
            var threshold = 100m;
            threshold = 2m;
            return ctx.Items.Where(x => x > threshold).Select(x => x * threshold).Sum();
            """, new() { AllowRuntimeLambdaBinding = true }).RequireRule();
        Assert.Equal(14m, rule.Execute(new(Values: [1m, 3m, 4m])));
    }

    [Theory]
    [InlineData("return 1;", "1")]
    [InlineData("return 0.1m + 0.2m;", "0.3")]
    [InlineData("return Math.Round(1.005m, 2, MidpointRounding.ToEven);", "1.00")]
    [InlineData("return Math.Round(-1.005m, 2, MidpointRounding.AwayFromZero);", "-1.01")]
    public void Decimal_conversion_and_rounding_use_upstream_type_system(string source, string expected)
        => Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            Compile<decimal>(source, new() { DecimalOnly = true }).RequireRule().Execute(new()));

    [Theory]
    [InlineData("var x = 1m; const y = x; y = 2m; return y;", "CRL2004")]
    [InlineData("var x = 1; { var x = 2; } return x;", "CRL2002")]
    [InlineData("{ var x = 1; } return x;", "CRL3001")]
    [InlineData("var x = x + 1; return x;", "CRL3001")]
    [InlineData("if (ctx.Vip) { return 1; }", "CRL2005")]
    [InlineData("return 1; var x = 2;", "CRL2006")]
    [InlineData("while (true) { return 1; }", "CRL2001")]
    [InlineData("ctx.Amount = 1m; return 1;", "CRL1001")]
    [InlineData("var x = null; return 1;", "CRL2003")]
    [InlineData("var x = 0; return (x = 1);", "CRL3001")]
    [InlineData("return DateTime.Now.Day;", "CRL4002")]
    [InlineData("return ctx.Items.Where(x => x.Bogus).Count();", "CRL3001")]
    public void Rejects_invalid_rules_with_structured_diagnostics(string text, string code)
    {
        var result = Compile<decimal>(text);
        Assert.False(result.Success);
        Assert.Null(result.Rule);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Const_is_runtime_readonly_not_compiletime_evaluation()
    {
        var rule = Compile<decimal>("const value = ctx.Amount; return value;").RequireRule();
        Assert.Equal(3m, rule.Execute(new(3m)));
        Assert.Equal(9m, rule.Execute(new(9m)));
    }

    [Theory]
    [InlineData("return 0.1 + 0.2;")]
    [InlineData("return (decimal)0.1;")]
    [InlineData("return Math.Sin(1);")]
    [InlineData("return 1 / 3;")]
    [InlineData("return 2147483647 + (int)ctx.Amount;")]
    public void Decimal_policy_rejects_float_in_any_subtree(string source)
    {
        var result = Compile<decimal>(source, new() { DecimalOnly = true });
        Assert.False(result.Success);
        // 上游可能先报告不兼容转换；无论哪一层拒绝均不可执行。
        Assert.Contains(result.Diagnostics[0].Code, new[] { "CRL3001", "CRL4002" });
    }

    [Theory]
    [InlineData("var x = 1m; return x / (ctx.Amount - ctx.Amount);")]
    [InlineData("return decimal.MaxValue * ctx.Amount;")]
    public void Execution_never_silently_converts_arithmetic_errors_to_zero(string source)
    {
        var rule = Compile<decimal>(source).RequireRule();
        Assert.ThrowsAny<ArithmeticException>(() => rule.Execute(new()));
    }

    [Fact]
    public void Comments_and_crlf_preserve_original_utf16_offsets()
    {
        const string text = "// 中文😀\r\nvar x = 1m; /* 😀 comment */\r\nreturn x + missing;";
        var diagnostic = Assert.Single(Compile<decimal>(text).Diagnostics);
        Assert.Equal(text.IndexOf("missing", StringComparison.Ordinal), diagnostic.Span.Start);
        Assert.Equal(3, diagnostic.Line);
        Assert.Equal(12, diagnostic.Column);
        Assert.Equal(PositionPrecision.Point, diagnostic.Precision);
    }

    [Theory]
    [InlineData("return \"/* ; { // }\";", "/* ; { // }")]
    [InlineData("return \"a\\\"b; //\";", "a\"b; //")]
    public void Literal_delimiters_do_not_break_statement_boundaries(string source, string expected)
        => Assert.Equal(expected, Compile<string>(source).RequireRule().Execute(new()));

    [Theory]
    [InlineData("return 1; /*")]
    [InlineData("return \"unterminated;")]
    [InlineData("return (1 + 2;")]
    [InlineData("return 1")]
    [InlineData("if (true) return 1;")]
    public void Malformed_boundaries_are_rejected(string text) => Assert.False(Compile<decimal>(text).Success);

    [Fact]
    public void Verbatim_string_boundary_is_scanned_but_upstream_syntax_is_not_extended()
        => Assert.Equal("CRL3001", Assert.Single(Compile<string>("return @\"a\"\"b; //\";").Diagnostics).Code);

    [Fact]
    public void More_than_sixteen_locals_do_not_require_fragment_func_delegates()
    {
        var declarations = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"var v{i} = {i}m;"));
        Assert.Equal(99m, Compile<decimal>(declarations + "return v99;").RequireRule().Execute(new()));
    }

    [Fact]
    public void Shared_compiler_and_delegate_are_isolated_across_concurrent_inputs()
    {
        var compiler = new RuleCompiler();
        var results = new ConcurrentBag<decimal>();
        var rule = compiler.Compile<Input, decimal>(new("parallel", "1", "var x = ctx.Amount; x = x + 1m; return x;")).RequireRule();
        Parallel.For(0, 10000, i => results.Add(rule.Execute(new(i))));
        Assert.Equal(Enumerable.Range(1, 10000).Select(i => (decimal)i), results.Order());
        Parallel.For(0, 32, _ => Assert.True(compiler.Compile<Input, decimal>(new("parallel", "1", "return ctx.Amount;")).Success));
    }

    [Fact]
    public void Exact_member_allowlist_checks_nested_calls()
    {
        var result = Compile<decimal>("return Math.Abs(ctx.Amount);", new() { IsMemberAllowed = m => m.DeclaringType != typeof(Math) });
        Assert.Equal("CRL4002", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resource_budgets_prevent_unbounded_syntax_work()
    {
        Assert.Equal("CRL4001", Compile<decimal>("return 1;", new() { MaxSourceLength = 3 }).Diagnostics[0].Code);
        Assert.Equal("CRL4001", Compile<decimal>("var a=1;return a;", new() { MaxStatements = 1 }).Diagnostics[0].Code);
        Assert.Equal("CRL4001", Compile<decimal>("var a=1;var b=2;return b;", new() { MaxLocals = 1 }).Diagnostics[0].Code);
        Assert.Equal("CRL4001", Compile<decimal>("{ { return 1; } }", new() { MaxDepth = 2 }).Diagnostics[0].Code);
    }

    [Fact]
    public void Source_policy_and_context_affect_fingerprint()
    {
        var a = Compile<decimal>("return 1;").RequireRule();
        var b = Compile<decimal>("return 1;", new() { PolicyVersion = "v2" }).RequireRule();
        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
        Assert.Equal(a.Fingerprint, Compile<decimal>("return 1;").RequireRule().Fingerprint);
    }

    [Fact]
    public void Compile_does_not_execute_context_getters()
    {
        var result = new RuleCompiler().Compile<ThrowingContext, decimal>(new("getter", "1", "return ctx.Amount;"));
        Assert.True(result.Success);
    }

    public sealed class ThrowingContext { public decimal Amount => throw new InvalidOperationException("must not execute while compiling"); }
}
