using System.Reflection;
using System.Text.Json;

namespace CRules.Tests;

public sealed record MetadataInput(RuleMetadata Metadata, bool Vip = true);
public sealed class Decision
{
    public string Action { get; init; } = "";
    public decimal Amount { get; set; }
    public long Rank;
}

public sealed class MetadataAndResultTests
{
    private static RuleCompilation<MetadataInput, T> Compile<T>(string source, RuleExecutionMode mode = RuleExecutionMode.JitCompiled,
        Func<MemberInfo, bool>? allowed = null)
        => new RuleCompiler(new() { DecimalOnly = true, ExecutionMode = mode,
            ReferencedTypes = [typeof(Decision)], IsMemberAllowed = allowed })
            .Compile<MetadataInput, T>(new("metadata-result", "1", source));

    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public void Typed_metadata_and_both_return_initializers_work_in_separate_branches(RuleExecutionMode mode)
    {
        var rule = Compile<Decision>("""
            var rate = ctx.Metadata.RequireDecimal("rate");
            if (ctx.Vip) {
                return new() { Action = "vip", Amount = rate * 0.9m, Rank = 1 };
            } else {
                return new Decision() { Action = "regular", Amount = rate, Rank = 2 };
            }
            """, mode).RequireRule();
        var metadata = RuleMetadata.FromJson("""{"rate":10} """);
        Assert.Equal(9m, rule.Execute(new(metadata)).Amount);
        var regular = rule.Execute(new(metadata, false));
        Assert.Equal(("regular", 10m, 2L), (regular.Action, regular.Amount, regular.Rank));
        Assert.NotSame(regular, rule.Execute(new(metadata, false)));
    }

    [Theory]
    [InlineData("return new() { Amount = 2m }; ")]
    [InlineData("return new () { Amount = 2m };")]
    [InlineData("return new /* keep positions */ () { Amount = 2m };")]
    [InlineData("return new Decision { Amount = 2m };")]
    [InlineData("var result = new Decision() { Amount = 2m }; return result;")]
    public void Object_creation_forms_preserve_result(string source)
        => Assert.Equal(2m, Compile<Decision>(source).RequireRule().Execute(new(RuleMetadata.Empty)).Amount);

    [Fact]
    public void Target_new_arguments_and_initializers_execute_once_in_order()
    {
        CounterDecision.Calls.Clear();
        var compiler = new RuleCompiler(new() { ReferencedTypes = [typeof(CounterDecision)] });
        var rule = compiler.Compile<MetadataInput, CounterDecision>(new("order", "1",
            "return new(7m) { First = CounterDecision.Next(), Second = CounterDecision.Next() }; ")).RequireRule();
        Assert.Empty(CounterDecision.Calls); // 编译不能调用构造、setter 或业务方法。
        var value = rule.Execute(new(RuleMetadata.Empty));
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
            .Compile<MetadataInput, ReadOnlyDecision>(new("invalid-init", "1", source));
        Assert.Equal("CRL3001", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Nested_explicit_initializer_strings_and_implicit_member_conversion()
    {
        var compiler = new RuleCompiler(new() { ReferencedTypes = [typeof(Envelope), typeof(Decision)] });
        var rule = compiler.Compile<MetadataInput, Envelope>(new("nested", "1", """
            return new() { Child = new Decision() { Action = "a,b}new()", Amount = 1, Rank = 2 } };
            """)).RequireRule();
        Assert.Equal("a,b}new()", rule.Execute(new(RuleMetadata.Empty)).Child!.Action);
        Assert.Equal(1m, rule.Execute(new(RuleMetadata.Empty)).Child!.Amount);
    }

    [Fact]
    public void Constructed_generic_result_uses_a_collision_free_target_type_alias()
    {
        var compiler = new RuleCompiler();
        var rule = compiler.Compile<MetadataInput, GenericDecision<decimal>>(new("generic", "1",
            "return new() { Value = 3m }; ")).RequireRule();
        Assert.Equal(3m, rule.Execute(new(RuleMetadata.Empty)).Value);
    }

    [Fact]
    public void Explicit_result_can_return_an_interface_and_nullable_member_values()
    {
        var compiler = new RuleCompiler(new() { ReferencedTypes = [typeof(DisposableDecision)] });
        var rule = compiler.Compile<MetadataInput, IDisposable>(new("interface", "1",
            "return new DisposableDecision() { Amount = null }; ")).RequireRule();
        using var value = (DisposableDecision)rule.Execute(new(RuleMetadata.Empty));
        Assert.Null(value.Amount);
    }

    [Fact]
    public void Collection_add_is_audited_even_inside_a_result_property()
    {
        var compiler = new RuleCompiler(new() { ReferencedTypes = [typeof(CollectionDecision), typeof(List<>)],
            IsMemberAllowed = member => member.Name != "Add" });
        var result = compiler.Compile<MetadataInput, CollectionDecision>(new("collection", "1",
            "return new() { Values = new List<decimal>() { 1m, 2m } };"));
        Assert.Equal("CRL4002", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Missing_null_and_wrong_type_are_distinct_and_defaults_are_explicit()
    {
        var metadata = RuleMetadata.FromJson("""{"nil":null,"secret":"not-a-number"}""");
        Assert.Equal(MetadataValueState.Missing, metadata.GetState("missing"));
        Assert.Equal(MetadataValueState.Null, metadata.GetState("nil"));
        Assert.False(metadata.Contains("missing"));
        Assert.True(metadata.Contains("nil"));
        Assert.True(metadata.IsNull("nil"));
        Assert.Null(metadata.GetDecimal("missing"));
        Assert.Null(metadata.GetDecimal("nil"));
        Assert.Equal(MetadataValueState.Missing, Assert.Throws<MetadataReadException>(() => metadata.RequireDecimal("missing")).State);
        var error = Assert.Throws<MetadataReadException>(() => metadata.GetDecimal("secret"));
        Assert.Equal(MetadataValueState.Value, error.State);
        Assert.DoesNotContain("not-a-number", error.Message);
        var rule = Compile<decimal>("return ctx.Metadata.GetDecimal(\"rate\") ?? 1m;").RequireRule();
        Assert.Equal(1m, rule.Execute(new(metadata)));
        Assert.Throws<MetadataReadException>(() => rule.Execute(new(RuleMetadata.FromJson("""{"rate":"1"}"""))));
    }

    [Fact]
    public void All_typed_reads_nested_objects_case_and_literal_keys()
    {
        var metadata = RuleMetadata.FromJson("""{"name":"vip","on":true,"count":9223372036854775807,"rate":0.1234567890123456789012345678,"child":{"x":2},"a.b":"literal"}""");
        Assert.Equal("vip", metadata.RequireString("name"));
        Assert.True(metadata.RequireBoolean("on"));
        Assert.Equal(long.MaxValue, metadata.RequireInt64("count"));
        Assert.Equal(0.1234567890123456789012345678m, metadata.RequireDecimal("rate"));
        Assert.Equal(2, metadata.RequireObject("child").RequireInt64("x"));
        Assert.Equal("literal", metadata.GetString("a.b"));
        Assert.Null(metadata.GetString("Name"));
        Assert.Null(metadata.GetObject("missing"));
        Assert.Throws<MetadataReadException>(() => metadata.GetBoolean("count"));
        Assert.Throws<MetadataReadException>(() => metadata.GetString("on"));
        Assert.Throws<MetadataReadException>(() => metadata.GetObject("name"));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("9223372036854775808")]
    [InlineData("\"1\"")]
    [InlineData("true")]
    public void Int64_never_truncates_coerces_or_overflows(string json)
        => Assert.Throws<MetadataReadException>(() => RuleMetadata.FromJson("{\"n\":" + json + "}").GetInt64("n"));

    [Fact]
    public void Decimal_overflow_and_input_limits_are_explicit()
    {
        Assert.Throws<MetadataReadException>(() => RuleMetadata.FromJson("{\"n\":1e100}").GetDecimal("n"));
        Assert.Throws<ArgumentException>(() => RuleMetadata.FromJson("[]"));
        Assert.Throws<ArgumentException>(() => RuleMetadata.FromJson("{\"x\":1,\"x\":2}"));
        Assert.Throws<ArgumentException>(() => RuleMetadata.FromJson("{\"s\":\"中\"}", maxBytes: 10));
        Assert.ThrowsAny<JsonException>(() => RuleMetadata.FromJson("{\"a\":{\"b\":1}}", maxDepth: 1));
    }

    [Fact]
    public async Task Snapshot_survives_document_disposal_dictionary_mutation_and_concurrent_execution()
    {
        RuleMetadata metadata;
        using (var doc = JsonDocument.Parse("{\"rate\":2}")) metadata = RuleMetadata.FromJson(doc.RootElement);
        Dictionary<string, object?> values = new() { ["rate"] = 3m };
        var copied = RuleMetadata.FromDictionary(values);
        values["rate"] = 99m;
        Assert.Equal(3m, copied.RequireDecimal("rate"));
        var rule = Compile<decimal>("return ctx.Metadata.RequireDecimal(\"rate\") * 2m;").RequireRule();
        var results = await Task.WhenAll(Enumerable.Range(0, 1000).Select(i => Task.Run(() => rule.Execute(new(i % 2 == 0 ? metadata : copied)))));
        for (int i = 0; i < results.Length; i++) Assert.Equal(i % 2 == 0 ? 4m : 6m, results[i]);
    }

    [Fact]
    public void Nested_branch_returns_are_valid_and_unknown_negative_conditions_need_a_guard()
    {
        var rule = Compile<decimal>("""
            var region = ctx.Metadata.GetString("region");
            if (ctx.Vip) {
                if (region != null && region != "restricted") { return 0.8m; }
                else { return 0.9m; }
            } else { return 1m; }
            """).RequireRule();
        Assert.Equal(0.9m, rule.Execute(new(RuleMetadata.Empty)));
        Assert.Equal(1m, rule.Execute(new(RuleMetadata.Empty, false)));
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
