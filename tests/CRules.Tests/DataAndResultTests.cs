using System.Reflection;
using System.Text.Json;

namespace CRules.Tests;

public sealed record DataInput(DataObject Data, bool Vip = true);
public sealed class Decision
{
    public string Action { get; init; } = "";
    public decimal Amount { get; set; }
    public long Rank;
}

public sealed class DataAndResultTests
{
    private static Compilation<Executable<Func<DataInput, T>>> Compile<T>(string source, RuleExecutionMode mode = RuleExecutionMode.JitCompiled,
        Func<MemberInfo, bool>? allowed = null)
        => new RuleCompiler(new()
        {
            DecimalOnly = true,
            ExecutionMode = mode,
            ReferencedTypes = [typeof(Decision)],
            IsMemberAllowed = allowed
        })
            .Compile<DataInput, T>(source);

    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public void Typed_data_and_both_return_initializers_work_in_separate_branches(RuleExecutionMode mode)
    {
        var rule = Compile<Decision>("""
            var rate = ctx.Data.RequireDecimal("rate");
            if (ctx.Vip) {
                return new() { Action = "vip", Amount = rate * 0.9m, Rank = 1 };
            } else {
                return new Decision() { Action = "regular", Amount = rate, Rank = 2 };
            }
            """, mode).RequireExecutable();
        var data = DataObject.FromJson("""{"rate":10} """);
        Assert.Equal(9m, rule.Execute(new(data)).Amount);
        var regular = rule.Execute(new(data, false));
        Assert.Equal(("regular", 10m, 2L), (regular.Action, regular.Amount, regular.Rank));
        Assert.NotSame(regular, rule.Execute(new(data, false)));
    }

    [Theory]
    [InlineData("return new() { Amount = 2m }; ")]
    [InlineData("return new () { Amount = 2m };")]
    [InlineData("return new /* keep positions */ () { Amount = 2m };")]
    [InlineData("return new Decision { Amount = 2m };")]
    [InlineData("var result = new Decision() { Amount = 2m }; return result;")]
    public void Object_creation_forms_preserve_result(string source)
        => Assert.Equal(2m, Compile<Decision>(source).RequireExecutable().Execute(new(DataObject.Empty)).Amount);

    [Fact]
    public void Target_new_arguments_and_initializers_execute_once_in_order()
    {
        CounterDecision.Calls.Clear();
        var compiler = new RuleCompiler(new() { ReferencedTypes = [typeof(CounterDecision)] });
        var rule = compiler.Compile<DataInput, CounterDecision>("return new(7m) { First = CounterDecision.Next(), Second = CounterDecision.Next() }; ").RequireExecutable();
        Assert.Empty(CounterDecision.Calls); // 编译不能调用构造、setter 或业务方法。
        var value = rule.Execute(new(DataObject.Empty));
        Assert.Equal(new[] { "ctor", "next", "first", "next", "second" }, CounterDecision.Calls);
        Assert.Equal(7m, value.Amount);
    }

    [Theory]
    [InlineData("return new() { Unknown = 1 };", "CRL3001")]
    [InlineData("return new() { Amount = \"invalid\" };", "CRL3001")]
    [InlineData("var x = new(); return x;", "CRL3001")]
    [InlineData("return new { Amount = 1m };", "CRL3001")]
    [InlineData("if (ctx.Vip) { return new() {}; }", "CRL2005")]
    [InlineData("return new() {}; var x = 1m;", "CRL2006")]
    public void Invalid_rules_report_original_source_positions(string source, string code)
    {
        var result = Compile<Decision>(source);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.InRange(error.Span.Start, 0, source.Length);
        Assert.InRange(error.Span.Start + error.Span.Length, 0, source.Length);
        Assert.DoesNotContain("__crules_result_type", error.Message);
    }

    [Fact]
    public void Rewritten_diagnostic_points_to_original_member()
    {
        const string source = "\nreturn new /*comment*/ () { Unknown = 1 };";
        var error = Assert.Single(Compile<Decision>(source).Diagnostics);
        Assert.Equal(source.IndexOf("Unknown", StringComparison.Ordinal), error.Span.Start);
        Assert.Equal(2, error.Line);
    }

    [Theory]
    [InlineData("Amount")]
    [InlineData("set_Amount")]
    [InlineData("Rank")]
    [InlineData(".ctor")]
    public void Initializer_targets_and_constructor_are_audited(string denied)
        => Assert.Equal("CRL4002", Assert.Single(Compile<Decision>(
            "return new() { Amount = 1m, Rank = 1 };", allowed: member => member.Name != denied).Diagnostics).Code);

    [Fact]
    public void Interface_and_object_results_cannot_infer_target_construction()
    {
        Assert.Equal("CRL2008", Assert.Single(Compile<IDisposable>("return new() {}; ").Diagnostics).Code);
        Assert.Equal("CRL2008", Assert.Single(Compile<object>("return new() {}; ").Diagnostics).Code);
    }

    [Theory]
    [InlineData("return new() { Amount = 1m, Amount = 2m }; ")]
    [InlineData("return new ReadOnlyDecision() { Value = 1m }; ")]
    [InlineData("return new ReadOnlyDecision() { Field = 1m }; ")]
    public void Duplicate_and_readonly_initializers_are_structured_failures(string source)
    {
        var result = new RuleCompiler(new() { ReferencedTypes = [typeof(ReadOnlyDecision)] })
            .Compile<DataInput, ReadOnlyDecision>(source);
        Assert.Equal("CRL3001", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Nested_explicit_initializer_strings_and_implicit_member_conversion()
    {
        var compiler = new RuleCompiler(new() { ReferencedTypes = [typeof(Envelope), typeof(Decision)] });
        var rule = compiler.Compile<DataInput, Envelope>("""
            return new() { Child = new Decision() { Action = "a,b}new()", Amount = 1, Rank = 2 } };
            """).RequireExecutable();
        Assert.Equal("a,b}new()", rule.Execute(new(DataObject.Empty)).Child!.Action);
        Assert.Equal(1m, rule.Execute(new(DataObject.Empty)).Child!.Amount);
    }

    [Fact]
    public void Constructed_generic_result_uses_a_collision_free_target_type_alias()
    {
        var compiler = new RuleCompiler();
        var rule = compiler.Compile<DataInput, GenericDecision<decimal>>("return new() { Value = 3m }; ").RequireExecutable();
        Assert.Equal(3m, rule.Execute(new(DataObject.Empty)).Value);
    }

    [Fact]
    public void Explicit_result_can_return_an_interface_and_nullable_member_values()
    {
        var compiler = new RuleCompiler(new() { ReferencedTypes = [typeof(DisposableDecision)] });
        var rule = compiler.Compile<DataInput, IDisposable>("return new DisposableDecision() { Amount = null }; ").RequireExecutable();
        using var value = (DisposableDecision)rule.Execute(new(DataObject.Empty));
        Assert.Null(value.Amount);
    }

    [Fact]
    public void Collection_add_is_audited_even_inside_a_result_property()
    {
        var compiler = new RuleCompiler(new()
        {
            ReferencedTypes = [typeof(CollectionDecision), typeof(List<>)],
            IsMemberAllowed = member => member.Name != "Add"
        });
        var result = compiler.Compile<DataInput, CollectionDecision>("return new() { Values = new List<decimal>() { 1m, 2m } };");
        Assert.Equal("CRL4002", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Missing_null_and_wrong_type_are_distinct_and_defaults_are_explicit()
    {
        var data = DataObject.FromJson("""{"nil":null,"secret":"not-a-number"}""");
        Assert.Equal(DataValueState.Missing, data.GetState("missing"));
        Assert.Equal(DataValueState.Null, data.GetState("nil"));
        Assert.False(data.Contains("missing"));
        Assert.True(data.Contains("nil"));
        Assert.True(data.IsNull("nil"));
        Assert.Null(data.GetDecimal("missing"));
        Assert.Null(data.GetDecimal("nil"));
        Assert.Equal(DataValueState.Missing, Assert.Throws<DataReadException>(() => data.RequireDecimal("missing")).State);
        var error = Assert.Throws<DataReadException>(() => data.GetDecimal("secret"));
        Assert.Equal(DataValueState.Value, error.State);
        Assert.DoesNotContain("not-a-number", error.Message);
        var rule = Compile<decimal>("return ctx.Data.GetDecimal(\"rate\") ?? 1m;").RequireExecutable();
        Assert.Equal(1m, rule.Execute(new(data)));
        Assert.Throws<DataReadException>(() => rule.Execute(new(DataObject.FromJson("""{"rate":"1"}"""))));
    }

    [Fact]
    public void All_typed_reads_nested_objects_case_and_literal_keys()
    {
        var data = DataObject.FromJson("""{"name":"vip","on":true,"count":9223372036854775807,"rate":0.1234567890123456789012345678,"child":{"x":2},"a.b":"literal"}""");
        Assert.Equal("vip", data.RequireString("name"));
        Assert.True(data.RequireBoolean("on"));
        Assert.Equal(long.MaxValue, data.RequireInt64("count"));
        Assert.Equal(0.1234567890123456789012345678m, data.RequireDecimal("rate"));
        Assert.Equal(2, data.RequireObject("child").RequireInt64("x"));
        Assert.Equal("literal", data.GetString("a.b"));
        Assert.Null(data.GetString("Name"));
        Assert.Null(data.GetObject("missing"));
        Assert.Throws<DataReadException>(() => data.GetBoolean("count"));
        Assert.Throws<DataReadException>(() => data.GetString("on"));
        Assert.Throws<DataReadException>(() => data.GetObject("name"));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("9223372036854775808")]
    [InlineData("\"1\"")]
    [InlineData("true")]
    public void Int64_never_truncates_coerces_or_overflows(string json)
        => Assert.Throws<DataReadException>(() => DataObject.FromJson("{\"n\":" + json + "}").GetInt64("n"));

    [Fact]
    public void Decimal_overflow_and_input_limits_are_explicit()
    {
        Assert.Throws<DataReadException>(() => DataObject.FromJson("{\"n\":1e100}").GetDecimal("n"));
        Assert.Throws<ArgumentException>(() => DataObject.FromJson("[]"));
        Assert.Throws<ArgumentException>(() => DataObject.FromJson("{\"x\":1,\"x\":2}"));
        Assert.Throws<ArgumentException>(() => DataObject.FromJson("{\"s\":\"中\"}", maxBytes: 10));
        Assert.ThrowsAny<JsonException>(() => DataObject.FromJson("{\"a\":{\"b\":1}}", maxDepth: 1));
    }

    [Fact]
    public async Task Snapshot_survives_document_disposal_dictionary_mutation_and_concurrent_execution()
    {
        DataObject data;
        using (var doc = JsonDocument.Parse("{\"rate\":2}")) data = DataObject.FromJson(doc.RootElement);
        Dictionary<string, object?> values = new() { ["rate"] = 3m };
        var copied = DataObject.FromDictionary(values);
        values["rate"] = 99m;
        Assert.Equal(3m, copied.RequireDecimal("rate"));
        var rule = Compile<decimal>("return ctx.Data.RequireDecimal(\"rate\") * 2m;").RequireExecutable();
        var results = await Task.WhenAll(Enumerable.Range(0, 1000).Select(i => Task.Run(() => rule.Execute(new(i % 2 == 0 ? data : copied)))));
        for (int i = 0; i < results.Length; i++) Assert.Equal(i % 2 == 0 ? 4m : 6m, results[i]);
    }

    [Fact]
    public void Nested_branch_returns_are_valid_and_unknown_negative_conditions_need_a_guard()
    {
        var rule = Compile<decimal>("""
            var region = ctx.Data.GetString("region");
            if (ctx.Vip) {
                if (region != null && region != "restricted") { return 0.8m; }
                else { return 0.9m; }
            } else { return 1m; }
            """).RequireExecutable();
        Assert.Equal(0.9m, rule.Execute(new(DataObject.Empty)));
        Assert.Equal(1m, rule.Execute(new(DataObject.Empty, false)));
    }
}

public sealed class CounterDecision
{
    public static List<string> Calls { get; } = [];
    public decimal Amount { get; }
    public CounterDecision(decimal amount) { Amount = amount; Calls.Add("ctor"); }
    public static decimal Next() { Calls.Add("next"); return 1m; }
    public decimal First { set => Calls.Add("first"); }
    public decimal Second { set => Calls.Add("second"); }
}

public sealed class ReadOnlyDecision
{
    public decimal Amount { get; set; }
    public decimal Value => 1m;
    public readonly decimal Field;
}
public sealed class Envelope { public Decision? Child { get; set; } }
public sealed class CollectionDecision { public List<decimal>? Values { get; set; } }
public sealed class GenericDecision<T> { public T? Value { get; set; } }
public sealed class DisposableDecision : IDisposable
{
    public decimal? Amount { get; set; }
    public void Dispose() { }
}
