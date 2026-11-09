namespace CRules.Tests;

public sealed class DifferentialTests
{
    [Fact]
    public void Randomized_rules_and_inputs_match_independent_native_logic()
    {
        var random = new Random(20261109);
        for (int n = 0; n < 50; n++)
        {
            int rate = random.Next(1, 20), cap = random.Next(10, 1000);
            var rule = new RuleCompiler().Compile<Input, decimal>(new("diff", n.ToString(), $$"""
                var value = ctx.Amount * {{rate}}m;
                if (ctx.Vip) { value = value * 0.9m; }
                if (value > {{cap}}m) { return {{cap}}m; }
                return value;
                """)).RequireRule();
            for (int i = 0; i < 100; i++)
            {
                var input = new Input(random.Next(-100, 1000) / 10m, random.Next(2) == 0);
                decimal expected = input.Amount * rate;
                if (input.Vip) expected *= 0.9m;
                Assert.Equal(Math.Min(expected, cap), rule.Execute(input));
            }
        }
    }

    [Fact]
    public void Nested_lambdas_capture_outer_lambda_and_rule_local()
    {
        var rule = new RuleCompiler(new() { AllowRuntimeLambdaBinding = true }).Compile<Input, bool>(new("nested", "1", """
            var threshold = 2m;
            return ctx.Items.Any(x => ctx.Items.Any(y => y > x + threshold));
            """)).RequireRule();
        Assert.True(rule.Execute(new(Values: [1m, 5m])));
        Assert.False(rule.Execute(new(Values: [1m, 2m])));
    }

    [Fact]
    public void Else_if_and_sibling_scope_reuse_are_well_defined()
    {
        var rule = new RuleCompiler().Compile<Input, decimal>(new("scope", "1", """
            var sum = 0m;
            { var x = 1m; sum = sum + x; }
            { var x = 2m; sum = sum + x; }
            if (ctx.Amount < 0m) { return -sum; }
            else if (ctx.Vip) { return sum * 2m; }
            else { return sum; }
            """)).RequireRule();
        Assert.Equal(-3m, rule.Execute(new(-1m)));
        Assert.Equal(6m, rule.Execute(new()));
        Assert.Equal(3m, rule.Execute(new(Vip: false)));
    }

    [Fact]
    public void Malformed_fuzz_never_returns_out_of_range_spans_or_hangs()
    {
        var random=new Random(19);
        const string alphabet="{}()[];/*'\"\\\r\n abc_123";
        for(int n=0;n<500;n++)
        {
            var text=new string(Enumerable.Range(0,random.Next(1,200)).Select(_=>alphabet[random.Next(alphabet.Length)]).ToArray());
            var result=new RuleCompiler().Compile<Input,decimal>(new("fuzz","1",text));
            foreach(var error in result.Diagnostics)
            {
                Assert.InRange(error.Span.Start,0,text.Length);
                Assert.InRange(error.Span.Length,0,text.Length-error.Span.Start);
            }
        }
    }
}
