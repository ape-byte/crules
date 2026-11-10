// Portions adapted from dotnet/roslyn, Copyright (c) .NET Foundation and Contributors.
// MIT license and pinned upstream provenance: Roslyn/NOTICE.md and inventory.json.
using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CRules.Tests;

public sealed class RoslynCompatibilityTests
{
    private static T[] Read<T>(string file) => JsonSerializer.Deserialize<T[]>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Roslyn", file)), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    private static readonly ImportedCase[] Cases = [.. Read<ImportedCase>("cases.json"), .. Read<ImportedCase>("curated.json"), .. Read<ImportedCase>("dictionary.json")];
    private static readonly Lazy<PortableExecutableReference[]> NativeReferences = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Append(typeof(RoslynCompatibilityTests).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path)).ToArray());
    private static readonly Lazy<Assembly> Native = new(() =>
    {
        string source = "using System; using System.Collections.Generic; using System.Linq; using CRules.Tests; public static class Oracle {\n" + string.Join("\n", Cases.Select(c =>
            $"public static {c.ReturnType} {c.Id}({string.Join(",", c.Parameters.Select(p => p.Type + " " + p.Name))}) {{ {c.Code} }}")) + "\n}";
        using var image = new MemoryStream();
        var result = CompileNative(source).Emit(image);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return Assembly.Load(image.ToArray());
    });

    public static IEnumerable<object[]> ImportedCases() => Cases.SelectMany(c => Enum.GetValues<RuleExecutionMode>().Select(mode => new object[] { c.Id, mode }));

    [Theory]
    [MemberData(nameof(ImportedCases))]
    public void Official_supported_function_cores_match_Roslyn(string id, RuleExecutionMode mode)
    {
        var spec = Cases.Single(c => c.Id == id);
        var method = Native.Value.GetType("Oracle")!.GetMethod(id)!;
        var nativeParameters = method.GetParameters();
        var compiled = new RuleCompiler(new() { ExecutionMode = mode, ReferencedTypes = [typeof(List<>), typeof(Dictionary<,>), typeof(StringComparer), typeof(MemberInitializerTest), typeof(MemberInitializerValue), typeof(DictionaryEvaluationProbe)] }).Compile(new CompileOptions
        {
            Code = spec.Code,
            ReturnType = method.ReturnType,
            Parameters = nativeParameters.Select(p => new InputParameter(p.Name!, p.ParameterType)).ToArray()
        });
        Assert.True(compiled.Success, $"{spec.File}::{spec.Test}/{spec.Method}\n{spec.Code}\n{string.Join("\n", compiled.Diagnostics)}");
        IEnumerable<object?[]> inputs = spec.InputProfile == "bounded_array_length"
            ? new object?[][] { [-1], [0], [1], [3] } : Inputs(nativeParameters.Select(p => p.ParameterType).ToArray());
        foreach (var input in inputs)
        {
            var expected = Outcome(() => method.Invoke(null, input));
            var actual = Outcome(() => compiled.RequireExecutable().Execute(input));
            Assert.Equal(expected.Error, actual.Error);
            if (expected.Error is not null) continue;
            EqualValue(expected.Value, actual.Value);
        }
    }

    private static void EqualValue(object? expected, object? actual)
    {
        if (expected is IDictionary dictionary)
        {
            var actualDictionary = Assert.IsAssignableFrom<IDictionary>(actual);
            Assert.Equal(dictionary.Keys.Cast<object>(), actualDictionary.Keys.Cast<object>());
            foreach (var key in dictionary.Keys) EqualValue(dictionary[key], actualDictionary[key]);
        }
        else if (expected is IEnumerable left && expected is not string)
        {
            var expectedItems = left.Cast<object?>().ToArray();
            var actualItems = Assert.IsAssignableFrom<IEnumerable>(actual).Cast<object?>().ToArray();
            Assert.Equal(expectedItems.Length, actualItems.Length);
            foreach (var pair in expectedItems.Zip(actualItems)) EqualValue(pair.First, pair.Second);
        }
        else if (expected?.GetType() == typeof(object)) Assert.IsType<object>(actual);
        else if (expected is MemberInitializerTest or MemberInitializerValue)
            Assert.Equal(JsonSerializer.Serialize(expected, new JsonSerializerOptions { IncludeFields = true }), JsonSerializer.Serialize(actual, new JsonSerializerOptions { IncludeFields = true }));
        else Assert.Equal(expected, actual);
    }

    private static (object? Value, Type? Error) Outcome(Func<object?> run)
    {
        try { return (run(), null); }
        catch (TargetInvocationException error) { return (null, error.InnerException!.GetType()); }
        catch (Exception error) { return (null, error.GetType()); }
    }
    private static IEnumerable<object?[]> Inputs(Type[] signature)
    {
        IEnumerable<object?[]> rows = [Array.Empty<object?>()];
        foreach (var type in signature)
            rows = rows.SelectMany(row => Values(type).Select(value => row.Append(value).ToArray())).Take(128).ToArray();
        return rows;
    }
    private static object?[] Values(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return [null, .. Values(nullable)];
        if (type == typeof(bool)) return [false, true];
        if (type == typeof(string)) return [null, "", "a", "b", "string", "abc", "hello", "1", "0", "A"];
        if (type == typeof(object)) return [null, "abc", 0, 1, true];
        if (type.IsArray) return [Array.CreateInstance(type.GetElementType()!, 0), Array.CreateInstance(type.GetElementType()!, 3)];
        var numeric = new decimal[] { -1, 0, 1, 2, 3, 4, 7, 42, 100, 255, 256, 32767 };
        List<object?> values = [];
        foreach (var value in numeric)
        {
            try { values.Add(Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture)); }
            catch (Exception error) when (error is OverflowException or InvalidCastException) { }
        }
        if (type == typeof(char)) return ['\0', 'a', 'b', '0', '1', char.MaxValue];
        foreach (var name in new[] { "MinValue", "MaxValue" })
            if (type.GetField(name)?.GetValue(null) is { } boundary) values.Add(boundary);
        return values.Distinct().ToArray();
    }
    private static CSharpCompilation CompileNative(string source) => CSharpCompilation.Create("CRulesOracle_" + Guid.NewGuid().ToString("N"),
        [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp9))],
        NativeReferences.Value,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

    public static IEnumerable<object[]> NegativeCases() => Read<NegativeCase>("negative.json").Select(c => new object[] { c.Id });
    [Theory]
    [MemberData(nameof(NegativeCases))]
    public void Official_negative_semantics_are_rejected(string id)
    {
        var spec = Read<NegativeCase>("negative.json").Single(c => c.Id == id);
        var native = CompileNative($"using CRules.Tests;public static class C {{public static {spec.ReturnType} M({spec.InputType} ctx) {{ {spec.Code} }} }}");
        Assert.Contains(native.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        Type TypeOf(string name) => name switch { "byte" => typeof(byte), "MemberInitializerTest" => typeof(MemberInitializerTest), _ => typeof(int) };
        var result = new RuleCompiler(new() { ReferencedTypes = [typeof(MemberInitializerTest)] }).Compile(new CompileOptions
        { Code = spec.Code, ReturnType = TypeOf(spec.ReturnType), Parameters = [new("ctx", TypeOf(spec.InputType))] });
        Assert.False(result.Success, $"{spec.File}::{spec.Test}\n{spec.Code}");
        Assert.NotEmpty(result.Diagnostics);
    }

    public sealed record ImportedCase(string Id, string File, string Test, string Method, string Code, string ReturnType, ImportedParameter[] Parameters, string? InputProfile = null);
    public sealed record ImportedParameter(string Name, string Type);
    public sealed record NegativeCase(string Id, string File, string Test, string Code, string InputType, string ReturnType);

    public static IEnumerable<object[]> DictionaryNegativeCases() => Read<string>("dictionary-negative.json")
        .SelectMany(code => Enum.GetValues<RuleExecutionMode>().Select(mode => new object[] { code, mode }));

    [Theory]
    [MemberData(nameof(DictionaryNegativeCases))]
    public void Dictionary_negative_semantics_are_rejected_by_both_compilers(string code, RuleExecutionMode mode)
    {
        var native = CompileNative($"using System.Collections.Generic; public static class C {{ public static Dictionary<string, decimal> M() {{ {code} }} }}");
        Assert.Contains(native.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        var result = new RuleCompiler(new() { ExecutionMode = mode }).Compile<Func<Dictionary<string, decimal>>>(code, []);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }
}

public sealed class DictionaryEvaluationProbe
{
    public string Log { get; private set; } = "";
    private decimal _value;
    public int Capacity { get { Log += "capacity;"; return 2; } }
    public string Key() { Log += "key;"; return "a"; }
    public decimal Value() { Log += "value;"; return ++_value; }
    private readonly Dictionary<string, decimal> _data = new() { ["a"] = 5m };
    public Dictionary<string, decimal> Data { get { Log += "receiver;"; return _data; } }
}

public sealed class MemberInitializerTest
{
    public int x;
    public int y { get; set; }
}
public struct MemberInitializerValue
{
    public int x;
    public int y { get; set; }
}
