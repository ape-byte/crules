namespace CRules.Tests;

public sealed class AssignmentExpressionTests
{
    public static IEnumerable<object[]> ValueCases() => new (string, int)[]
    {
        ("var x = 0; var y = 0; var n = (x = y = 3); return n * 100 + x * 10 + y;", 333),
        ("var x = 0; return (x = 3);", 3),
        ("byte x = 0; return ctx.Overload(x = 255);", 1),
        ("var x = 0; var n = x = x == 0 ? x = 3 : x = 4; return n * 10 + x;", 33),
        ("var x = 0; var n = false ? x = 2 : true ? x = 5 : x = 6; return n * 10 + x;", 55),
        ("var x = 0; if (false && (x = 2) == 2) return -1; if (!(true || (x = 3) == 3)) return -2; return x;", 0),
        ("int? first = 1; var x = 0; var n = first ?? (x = 2); return n * 10 + x;", 10),
        ("int? first = null; var x = 0; var n = first ?? (x = 2); return n * 10 + x;", 22),
        ("var x = 0; var n = Math.Max(x = 3, x = 2); return n * 10 + x;", 32),
        ("var x = 0; var n = Math.Max(1 < 2 ? x = 3 : x = 4, x = 2); return n * 10 + x;", 32),
        ("var x = 1; (x) = 2; return x;", 2),
        ("var x = 0; var d = new AssignmentDecision(x = 2) { Value = x = 3 }; return d.Seed * 100 + d.Value * 10 + x;", 233),
        ("var x = 0; var values = new[] { x = 3, x = 2 }; return values[0] * 100 + values[1] * 10 + x;", 322),
        ("var x = 0; var n = x = 1 switch { 1 => x = 3, _ => x = 2 }; return n * 10 + x;", 33),
        ("var d = new Dictionary<string, AssignmentDecision>(); var n = (d[\"a\"] = new() { Value = 3 }); return n.Value;", 3),
        ("var d = new AssignmentDecision { Value = 1 }; d.Value = 4; return (d.Value = d.Value + 2);", 6),
        ("var d = new AssignmentDecision(); var n = (d.Delay = 60); return n.Value;", 60),
        ("var d = new AssignmentDecision(); d.Delay = 60; return (d.Delay = null) == null ? 1 : 0;", 1),
        ("var d = new AssignmentDecision(); var n = (d.Normalized = 3); return n * 10 + d.Normalized;", 34),
        ("var d = new AssignmentDecision(); var n = (d.WriteOnly = 3); return n * 10 + d.Value;", 33),
        ("var d = new AssignmentDecision { Value = 1 }; var old = d; var n = d.Value = (d = new AssignmentDecision { Value = 2 }).Value; return n * 100 + old.Value * 10 + d.Value;", 222),
        ("AssignmentDecision d = new(); if (ctx.Input.Value == 0) d = new(); return (d.Value = 3);", 3),
        ("var d = new Dictionary<string, int>(); int x = 0; return (d[\"=\"] = x = 3) + x + d[\"=\"];", 9),
        ("ctx.Values[\"a\"] = 4; ctx.Flags[ctx.Values.TryGetValue(\"a\", out var n)] = n; return ctx.Flags[true];", 4),
        ("var x = 0; return Math.Max((x /* = */ = 3), x);", 3),
    }.SelectMany(item => Enum.GetValues<RuleExecutionMode>().Select(mode => new object[] { mode, item.Item1, item.Item2 }));

    [Theory]
    [MemberData(nameof(ValueCases))]
    public void Simple_assignments_are_typed_values(RuleExecutionMode mode, string source, int expected)
    {
        var compiled = Compiler(mode).Compile<AssignmentProbe, int>(source);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        Assert.Equal(expected, compiled.RequireExecutable().Execute(new()));
    }

    [Theory]
    [InlineData(RuleExecutionMode.JitCompiled)]
    [InlineData(RuleExecutionMode.Interpreted)]
    public void Dictionary_targets_and_rhs_are_evaluated_once_in_order(RuleExecutionMode mode)
    {
        var input = new AssignmentProbe();
        var rule = Compiler(mode).Compile<AssignmentProbe, int>("""
            var n = ctx.GetTarget()[ctx.GetKey()] = ctx.GetTarget()[ctx.GetKey()] = ctx.Next();
            return n;
            """).RequireExecutable();
        Assert.Equal(7, rule.Execute(input));
        Assert.Equal("target;key;target;key;rhs;", input.Log);
        Assert.Equal(7, input.Values["key"]);
    }

    public static IEnumerable<object[]> InvalidCases() => new (string, string)[]
    {
        ("const x = 1; return (x = 2);", "CRL2004"),
        ("return (ctx = new AssignmentProbe()).Input.Value;", "CRL2003"),
        ("return (ctx.Input.Value = 2);", "CRL2003"),
        ("var d = ctx.Input; return (d.Value = 2);", "CRL2003"),
        ("var d = new AssignmentDecision(); var alias = d; return (alias.Value = 2);", "CRL2003"),
        ("var d = new AssignmentDecision(); if (ctx.Input.Value == 1) d = ctx.Input; return (d.Value = 2);", "CRL2003"),
        ("var d = new AssignmentDecision(); var n = (d.Value = 2); d = ctx.Input; return n;", "CRL2003"),
        ("var d = new AssignmentDecision(); var n = (d = ctx.Input).Value; return (d.Value = n);", "CRL2003"),
        ("var d = new AssignmentDecision(); ctx.Decisions.TryGetValue(\"a\", out d); return (d.Value = 2);", "CRL2003"),
        ("var d = new AssignmentDecision(); return (d.InitOnly = 2);", "CRL2003"),
        ("var d = new AssignmentDecision(); return (d.PrivateSetter = 2);", "CRL2003"),
        ("var d = new AssignmentDecision(); return (d.Field = 2);", "CRL2003"),
        ("var d = new AssignmentDecision(); return (d.Child.Value = 2);", "CRL2003"),
        ("var d = new AssignmentDecision(); d.set_InitOnly(2); return d.InitOnly;", "CRL4002"),
        ("var d = new AssignmentDecision(); d.set_Value(2); return d.Value;", "CRL4002"),
        ("var d = new List<int>() { 0 }; return (d[0] = 2);", "CRL2003"),
        ("var d = new[] { 0 }; return (d[0] = 2);", "CRL2003"),
        ("return (ctx.ReadOnly[\"a\"] = 2);", "CRL2003"),
        ("byte n = 0; return (n = 256);", "CRL3001"),
        ("byte n = 0; var x = 2; return (n = (x = 3));", "CRL3001"),
        ("var x = 0; return (x += 2);", "CRL3001"),
        ("int? x = null; return (x ??= 2).Value;", "CRL3001"),
    }.SelectMany(item => Enum.GetValues<RuleExecutionMode>().Select(mode => new object[] { mode, item.Item1, item.Item2 }));

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Invalid_targets_and_values_are_diagnosed(RuleExecutionMode mode, string source, string code)
    {
        var result = Compiler(mode).Compile<AssignmentProbe, int>(source);
        Assert.False(result.Success, source);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("Value")]
    [InlineData("set_Value")]
    [InlineData("Item")]
    [InlineData("set_Item")]
    public void Value_producing_writes_audit_the_member_and_setter(string denied)
    {
        var compiler = new RuleCompiler(new() { ReferencedTypes = [typeof(AssignmentDecision)], IsMemberAllowed = member => member.Name != denied });
        string body = denied.EndsWith("Item")
            ? "return (ctx.Values[\"a\"] = 2);"
            : "var d = new AssignmentDecision(); return (d.Value = 2);";
        Assert.Equal("CRL4002", Assert.Single(compiler.Compile<AssignmentProbe, int>(body).Diagnostics).Code);
    }

    [Fact]
    public void Long_unparenthesized_assignment_chains_respect_the_depth_limit()
    {
        var compiler = new RuleCompiler(new() { MaxDepth = 4 });
        Assert.False(compiler.Compile<AssignmentProbe, int>("var x = 0; return x = x = x = x = x = 3;").Success);
        Assert.False(compiler.Compile<AssignmentProbe, int>("var x = 0; return true ? 1 : true ? 1 : true ? 1 : true ? 1 : true ? 1 : x = 2;").Success);
    }

    private static RuleCompiler Compiler(RuleExecutionMode mode) => new(new()
    {
        ExecutionMode = mode,
        ReferencedTypes = [typeof(AssignmentDecision), typeof(Dictionary<,>), typeof(List<>)]
    });
}

public sealed class AssignmentDecision(int seed)
{
    public AssignmentDecision() : this(0) { }
    private int _normalized;
    public int Seed { get; } = seed;
    public int Value { get; set; }
    public int? Delay { get; set; }
    public int InitOnly { get; init; }
    public int PrivateSetter { get; private set; }
    public int Field;
    public AssignmentDecision Child => this;
    public int Normalized { get => _normalized; set => _normalized = value + 1; }
    public int WriteOnly { set => Value = value; }
}

public sealed class AssignmentProbe
{
    public AssignmentDecision Input { get; } = new();
    public Dictionary<string, AssignmentDecision> Decisions { get; } = new();
    public Dictionary<string, int> Values { get; } = new();
    public Dictionary<bool, int> Flags { get; } = new();
    public IReadOnlyDictionary<string, int> ReadOnly => Values;
    public string Log { get; private set; } = "";
    public int Overload(byte value) => 1;
    public int Overload(int value) => 2;
    public Dictionary<string, int> GetTarget() { Log += "target;"; return Values; }
    public string GetKey() { Log += "key;"; return "key"; }
    public int Next() { Log += "rhs;"; return 7; }
}
