using System.Linq.Expressions;
using DynamicExpresso;
using DynamicExpresso.Exceptions;

namespace CRules.Tests;

public sealed class PublicApiTests
{
    [Fact]
    public void Parameter_identity_and_inference_use_public_api()
    {
        var local = Expression.Variable(typeof(decimal), "total");
        var interpreter = new Interpreter().EnableAssignment(AssignmentOperators.None);
        var parsed = interpreter.Parse("total * 0.9m", new Parameter(local));
        Assert.Equal(typeof(decimal), parsed.ReturnType);
        Assert.Same(local, Assert.IsAssignableFrom<BinaryExpression>(parsed.Expression).Left);
        Assert.Same(local, interpreter.SetExpression("v", local).Parse("v").Expression);
        Assert.ThrowsAny<ParseException>(() => interpreter.Parse("total = 1m", new Parameter(local)));
    }
    [Fact]
    public void Public_expression_tree_and_expected_type_work()
    {
        var tree = new Interpreter().ParseAsExpression<Func<Input, decimal>>("ctx.Amount + 1m", "ctx");
        Assert.Equal(101m, tree.Compile()(new()));
        Assert.Equal(typeof(decimal), new Interpreter().Parse("1", typeof(decimal)).ReturnType);
    }
    [Fact]
    public void Lambda_name_collision_is_a_documented_rejection()
    {
        var result = new RuleCompiler().Compile<Input, decimal>(new("collision", "1",
            "var total = 2m; return ctx.Items.Where(total => total > 1m).Sum();"));
        Assert.Equal("CRL3001", Assert.Single(result.Diagnostics).Code);
        Assert.Equal(PositionPrecision.Expression, result.Diagnostics[0].Precision);
    }
    [Fact]
    public void Scalar_hot_execution_allocates_zero_per_call()
    {
        var rule = new RuleCompiler().Compile<Input, decimal>(new("alloc", "1", "var x=ctx.Amount*0.8m;return x;")).RequireRule();
        var input = new Input(); decimal checksum=0m;
        for(int i=0;i<10000;i++) checksum+=rule.Execute(input);
        long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++) checksum+=rule.Execute(input);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-start;
        Assert.Equal(0,allocated);Assert.Equal(1600000m,checksum);
    }

    [Fact]
    public void Strict_mode_does_not_claim_nested_lambdas_have_reflection_free_binding()
    {
        const string text="return ctx.Items.Where(x => x > ctx.Amount).Sum();";
        var strict=new RuleCompiler().Compile<Input,decimal>(new("lambda","1",text));
        Assert.Equal("CRL4003",Assert.Single(strict.Diagnostics).Code);
        var experiment=new RuleCompiler(new(){AllowRuntimeLambdaBinding=true})
            .Compile<Input,decimal>(new("lambda","1",text)).RequireRule();
        Assert.Equal(5m,experiment.Execute(new(2m,Values:[1m,5m])));
    }
}
