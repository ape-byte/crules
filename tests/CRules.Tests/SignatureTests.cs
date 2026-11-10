using System.Dynamic;

namespace CRules.Tests;

public sealed class SignatureTests
{
    [Fact]
    public void Named_multi_parameter_typed_and_runtime_paths_agree()
    {
        var compiler = new RuleCompiler();
        const string code = "if (amount > 100m) { return amount * rate; } else { return amount; }";
        var typed = compiler.Compile<Func<decimal, decimal, decimal>>(code, ["amount", "rate"], "Price").RequireExecutable();
        var runtime = compiler.Compile(new CompileOptions { Code = code, Name = "Price", ReturnType = typeof(decimal), Parameters = [new("amount", typeof(decimal)), new("rate", typeof(decimal))] }).RequireExecutable();
        Assert.Equal(180m, typed.Execute(200m, 0.9m));
        Assert.Equal(typed.Execute(200m, 0.9m), runtime.Execute(200m, 0.9m));
        Assert.Equal("Price", typed.Name);
        Assert.Throws<ArgumentException>(() => runtime.Execute(1));
        Assert.Throws<ArgumentException>(() => runtime.Execute(null, 0.9m));
        Assert.Throws<ArgumentException>(() => runtime.Execute("200", 0.9m));
    }
    [Fact]
    public void Zero_inputs_custom_name_and_dynamic_input()
    {
        var compiler = new RuleCompiler();
        Assert.Equal(3m, compiler.Compile<Func<decimal>>("return 3m;", []).RequireExecutable().Execute());
        Assert.Equal(4m, compiler.Compile<decimal, decimal>("return amount;", "amount").RequireExecutable().Execute(4m));
        var rule = compiler.Compile<Func<ExpandoObject, decimal, decimal>>("return (decimal)data.Amount * rate;", ["data", "rate"]).RequireExecutable();
        dynamic data = new ExpandoObject(); data.Amount = 10m;
        Assert.Equal(9m, rule.Execute((ExpandoObject)data, 0.9m));
        data.Amount = 20m;
        Assert.Equal(18m, rule.Execute((ExpandoObject)data, 0.9m));
        Assert.ThrowsAny<Exception>(() => rule.Execute(new ExpandoObject(), 0.9m));
    }
    [Theory]
    [InlineData("x", "x")]
    [InlineData("if", "rate")]
    [InlineData("bad-name", "rate")]
    [InlineData("true", "rate")]
    [InlineData("int", "rate")]
    public void Invalid_names_are_compilation_diagnostics(string a, string b)
        => Assert.False(new RuleCompiler().Compile<Func<decimal, decimal, decimal>>("return 1m;", [a, b]).Success);

    [Fact]
    public async Task Task_value_task_and_sync_results_have_async_entry()
    {
        var compiler = new RuleCompiler();
        var task = compiler.Compile<AsyncService, Task<decimal>>("return ctx.GetAsync();").RequireExecutable();
        Assert.Equal(7m, await task.ExecuteAsync(new AsyncService()));
        Assert.Equal(7m, await task.Execute(new AsyncService()));
        var vt = compiler.Compile<AsyncService, ValueTask<decimal>>("return ctx.ValueAsync();").RequireExecutable();
        Assert.Equal(8m, await vt.ExecuteAsync(new AsyncService()));
        var sync = compiler.Compile<Func<decimal>>("return 3m;", []).RequireExecutable();
        Assert.Equal(3m, await sync.ExecuteAsync());
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sync.ExecuteAsync([], cts.Token));
        var failure = compiler.Compile<AsyncService, Task<decimal>>("return ctx.FailAsync();").RequireExecutable();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await failure.ExecuteAsync(new AsyncService()));
    }
    [Fact]
    public void Dynamic_decimal_inputs_reject_binary_floating_point()
    {
        var rule = new RuleCompiler(new() { DecimalOnly = true }).Compile<ExpandoObject, decimal>("return (decimal)ctx.Amount;").RequireExecutable();
        dynamic input = new ExpandoObject(); input.Amount = 0.1d;
        Assert.Throws<InvalidOperationException>(() => rule.Execute((ExpandoObject)input));
    }
    [Fact]
    public void Input_names_cannot_be_shadowed_by_locals()
        => Assert.False(new RuleCompiler().Compile<decimal, decimal>("var amount=2m;return amount;", "amount").Success);

    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public void Dynamic_branch_and_arithmetic_work_without_a_business_wrapper(RuleExecutionMode mode)
    {
        var rule = new RuleCompiler(new() { ExecutionMode = mode }).Compile<ExpandoObject, decimal>(
            "if(ctx.IsVip){return ctx.Amount * ctx.Discount;}else{return ctx.Amount;}").RequireExecutable();
        dynamic data = new ExpandoObject(); data.IsVip = true; data.Amount = 100m; data.Discount = 0.9m;
        Assert.Equal(90m, rule.Execute((ExpandoObject)data));
        data.IsVip = false; Assert.Equal(100m, rule.Execute((ExpandoObject)data));
    }
    [Fact]
    public void Static_member_policy_cannot_be_bypassed_via_dynamic_access()
        => Assert.False(new RuleCompiler(new() { IsMemberAllowed = _ => true }).Compile<ExpandoObject, decimal>("return (decimal)ctx.Amount;").Success);
    [Fact]
    public void Dynamic_decimal_arithmetic_requires_explicit_decimal_conversion()
        => Assert.False(new RuleCompiler(new() { DecimalOnly = true }).Compile<ExpandoObject, decimal>("return ctx.Amount + ctx.Amount;").Success);
    [Fact]
    public void Incomplete_delegate_signatures_and_unsupported_types_fail()
    {
        Assert.Throws<ArgumentException>(() => new RuleCompiler().Compile<Func<decimal, decimal>>("return 1m;", []));
        Assert.False(new RuleCompiler().Compile(new CompileOptions { Code = "return 1m;", ReturnType = typeof(void) }).Success);
        Assert.False(new RuleCompiler().Compile<Func<decimal>>("return 1m;", [], "anonymous").RequireExecutable().Parameters.Any());
    }
    [Fact]
    public async Task Cancellation_can_be_passed_to_async_host_method()
    {
        var rule = new RuleCompiler().Compile<Func<AsyncService, CancellationToken, Task<decimal>>>(
            "return service.CancelAsync(token);", ["service", "token"]).RequireExecutable();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await rule.ExecuteAsync(new AsyncService(), cts.Token));
    }

    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public async Task Non_generic_async_returns_are_awaited_in_both_entry_forms(RuleExecutionMode mode)
    {
        var compiler = new RuleCompiler(new() { ExecutionMode = mode });
        var service = new AsyncService();
        Assert.Null(await compiler.Compile<AsyncService, Task>("return ctx.CompleteAsync();").RequireExecutable().ExecuteAsync(service));
        Assert.Null(await compiler.Compile(new CompileOptions { Code = "return service.CompleteValueAsync();", ReturnType = typeof(ValueTask), Parameters = [new("service", typeof(AsyncService))] }).RequireExecutable().ExecuteAsync(service));
        Assert.Equal(2, service.Completed);
    }
}
public sealed class AsyncService
{
    public int Completed { get; private set; }
    public async Task CompleteAsync() { await Task.Yield(); Completed++; }
    public async ValueTask CompleteValueAsync() { await Task.Yield(); Completed++; }
    public async Task<decimal> GetAsync() { await Task.Yield(); return 7m; }
    public ValueTask<decimal> ValueAsync() => ValueTask.FromResult(8m);
    public Task<decimal> FailAsync() => Task.FromException<decimal>(new InvalidOperationException("failure"));
    public Task<decimal> CancelAsync(CancellationToken token) => Task.FromCanceled<decimal>(token);
}
