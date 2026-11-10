namespace CRules.Tests;

public sealed class InterpretationTests
{
    [Fact]
    public void Explicit_interpreter_mode_is_visible_and_matches_jit_for_scalar_rules()
    {
        const string source = "var x=ctx.Amount*0.8m;if(ctx.Vip){x=x*0.9m;}return Math.Round(x,2,MidpointRounding.ToEven);";
        var compiled = new RuleCompiler().Compile<Input, decimal>(source).RequireExecutable();
        var interpreted = new RuleCompiler(new() { ExecutionMode = RuleExecutionMode.Interpreted })
            .Compile<Input, decimal>(source).RequireExecutable();
        Assert.Equal(RuleExecutionMode.JitCompiled, compiled.ExecutionMode);
        Assert.Equal(RuleExecutionMode.Interpreted, interpreted.ExecutionMode);
        foreach (var input in new[] { new Input(), new Input(13.25m, false) })
            Assert.Equal(compiled.Execute(input), interpreted.Execute(input));
    }
}
