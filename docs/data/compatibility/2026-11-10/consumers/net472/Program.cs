using CRules;
foreach(var mode in (RuleExecutionMode[])Enum.GetValues(typeof(RuleExecutionMode))) {
    var compiler = new RuleCompiler(new() { ExecutionMode = mode });
    var dictionary = compiler.Compile<Func<decimal, decimal, Dictionary<string,decimal>>>("""
        var source = new Dictionary<string, decimal>() { {"amount", amount}, {"rate", rate} };
        Dictionary<string,decimal> result = new(source) { ["extra"] = 1m };
        result["amount"] *= rate;
        if(result.TryGetValue("amount", out var value)) { result["calculated"] = value; }
        return result;
        """, ["amount","rate"]).RequireExecutable();
    if(dictionary.Execute(100m,0.9m)["calculated"] != 90m)throw new Exception("dictionary");
    var runtime = compiler.Compile(new CompileOptions { Code="return amount*rate;", ReturnType=typeof(decimal), Parameters=[new("amount",typeof(decimal)),new("rate",typeof(decimal))] }).RequireExecutable();
    if(!Equals(runtime.Execute(100m,0.9m),90m))throw new Exception("runtime");
    if(compiler.Compile<Func<decimal?>>("return null;",[]).RequireExecutable().Execute() is not null)throw new Exception("null");
    if(compiler.Compile<Func<Price>>("return new Price { Amount=2m };",[]).RequireExecutable().Execute().Amount!=2m)throw new Exception("DTO");
    var task = compiler.Compile<Service, Task<decimal?>>("return ctx.Calculate();").RequireExecutable();
    if(!Equals(await task.ExecuteAsync(new Service()),2m))throw new Exception("async");
    var data = compiler.Compile<DataObject,decimal>("return ctx.RequireDecimal(\"rate\");").RequireExecutable();
    if(data.Execute(DataObject.FromJson("{\"rate\":0.9}"))!=0.9m)throw new Exception("data");
    Console.WriteLine($"PASS package consumer {mode}: 6 checks");
}
Console.WriteLine("NUGET_CONSUMER_PASS");
public sealed class Price { public decimal Amount {get;set;} }
public sealed class Service { public Task<decimal?> Calculate()=>Task.FromResult<decimal?>(2m); }
