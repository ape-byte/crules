using System.Dynamic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace CRules;

public sealed class RuleCompiler : IRuleCompiler
{
    private readonly CompilerOptions _options;
    public RuleCompiler(CompilerOptions? options = null)
    {
        var input = options ?? new();
        _options = input with { ReferencedTypes = input.ReferencedTypes.ToArray() };
        if (!Enum.IsDefined(_options.ExecutionMode) || _options.MaxSourceLength <= 0 || _options.MaxStatements <= 0 ||
            _options.MaxDepth <= 0 || _options.MaxLocals <= 0 || _options.MaxExpressionLength <= 0 || _options.MaxTreeNodes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
    }
    public Compilation<Executable<Func<TInput, TResult>>> Compile<TInput, TResult>(string code, string parameterName = "ctx", string? name = null)
        => Compile<Func<TInput, TResult>>(code, [parameterName], name);
    public Compilation<Executable<TDelegate>> Compile<TDelegate>(string code, string[] parameterNames, string? name = null) where TDelegate : Delegate
    {
        var signature = typeof(TDelegate).GetMethod("Invoke") ?? throw new ArgumentException("需要具体委托类型。");
        ArgumentNullException.ThrowIfNull(parameterNames);
        var args = signature.GetParameters();
        if (args.Length != parameterNames.Length) throw new ArgumentException("形参名称数量与委托签名不一致。");
        var spec = new CompileOptions
        {
            Code = code,
            Name = name,
            ReturnType = signature.ReturnType,
            Parameters = args.Select((p, i) => new InputParameter(parameterNames[i], p.ParameterType)).ToArray()
        };
        try
        {
            var tree = Build(spec);
            var typed = Expression.Lambda<TDelegate>(tree.Body, name, false, tree.Parameters).Compile(_options.ExecutionMode == RuleExecutionMode.Interpreted);
            return new(new(spec, _options.ExecutionMode, typed, Adapter(tree, typed), Awaiter(spec.ReturnType)), []);
        }
        catch (CompileFailure failure) { return new(null, [Diagnostic(code, failure)]); }
    }
    public Compilation<Executable> Compile(CompileOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Parameters);
        var snapshot = new CompileOptions { Code = options.Code, Name = options.Name, ReturnType = options.ReturnType, Parameters = options.Parameters.ToArray() };
        try { var tree = Build(snapshot); return new(new(snapshot, _options.ExecutionMode, Adapter(tree), Awaiter(snapshot.ReturnType)), []); }
        catch (CompileFailure failure) { return new(null, [Diagnostic(snapshot.Code, failure)]); }
    }
    private LambdaExpression Build(CompileOptions spec)
    {
        ArgumentNullException.ThrowIfNull(spec.Code);
        ArgumentNullException.ThrowIfNull(spec.ReturnType);
        if (_options.ExecutionMode == RuleExecutionMode.JitCompiled && !RuntimeFeature.IsDynamicCodeSupported)
            throw new CompileFailure("CRL4004", "当前运行时需要显式选择 Interpreted。", new(0, 0));
        if (spec.Code.Length > _options.MaxSourceLength) throw new CompileFailure("CRL4001", "源码超过限额。", new(0, spec.Code.Length));
        bool Invalid(Type t) => t == typeof(void) || t.IsByRef || t.IsPointer || t.IsByRefLike || t.ContainsGenericParameters;
        if (Invalid(spec.ReturnType) || typeof(Delegate).IsAssignableFrom(spec.ReturnType) || typeof(Expression).IsAssignableFrom(spec.ReturnType) ||
            typeof(System.Collections.IEnumerable).IsAssignableFrom(spec.ReturnType) && spec.ReturnType != typeof(string)
            && !spec.ReturnType.IsArray && !(spec.ReturnType.IsGenericType && spec.ReturnType.GetGenericTypeDefinition() == typeof(List<>)))
            throw new CompileFailure("CRL2007", "不支持的返回类型。", new(0, 0));
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (var p in spec.Parameters)
        {
            ArgumentNullException.ThrowIfNull(p);
            ArgumentNullException.ThrowIfNull(p.Type);
            if (string.IsNullOrEmpty(p.Name) || !(char.IsLetter(p.Name[0]) || p.Name[0] == '_') || p.Name.Any(c => !(char.IsLetterOrDigit(c) || c == '_')) ||
                SyntaxNames.IsReserved(p.Name, _options.ReferencedTypes) || !names.Add(p.Name) || Invalid(p.Type))
                throw new CompileFailure("CRL2002", "形参名称或类型无效、重复或占用保留名称。", new(0, 0));
        }
        var syntax = new StatementParser(spec.Code, _options);
        var parameters = spec.Parameters.Select(p => Expression.Parameter(p.Type, p.Name)).ToArray();
        var tree = new TreeBuilder(syntax.Text, _options, spec.ReturnType, parameters).Build(syntax.Parse());
        new ExpressionAudit(_options, new(0, spec.Code.Length), true).Visit(tree);
        if (_options.DecimalOnly)
        {
            var guards = parameters.Where(p => typeof(ExpandoObject).IsAssignableFrom(p.Type))
                .Select(p => (Expression)Expression.Call(typeof(RuleCompiler), nameof(ValidateDynamicNumbers), null, p)).ToList();
            if (guards.Count > 0) tree = Expression.Lambda(Expression.Block(guards.Append(tree.Body)), parameters);
        }
        return tree;
    }
    public static void ValidateDynamicNumbers(ExpandoObject value)
    {
        HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
        void Walk(object? item)
        {
            if (item is float or double) throw new InvalidOperationException("DecimalOnly 动态输入不接受 float/double。");
            if (item is ExpandoObject obj && seen.Add(obj)) foreach (var child in (IDictionary<string, object?>)obj) Walk(child.Value);
        }
        Walk(value);
    }
    private Func<object?[], object?> Adapter(LambdaExpression tree, Delegate? compiled = null)
    {
        var args = Expression.Parameter(typeof(object[]), "arguments");
        compiled ??= tree.Compile(_options.ExecutionMode == RuleExecutionMode.Interpreted);
        var values = tree.Parameters.Select((p, i) => (Expression)Expression.Convert(Expression.ArrayIndex(args, Expression.Constant(i)), p.Type));
        var call = Expression.Invoke(Expression.Constant(compiled, tree.Type), values);
        Expression body = call.Type == typeof(object) ? call : Expression.Convert(call, typeof(object));
        return Expression.Lambda<Func<object?[], object?>>(body, args).Compile(_options.ExecutionMode == RuleExecutionMode.Interpreted);
    }
    private Func<object?, ValueTask<object?>> Awaiter(Type type)
    {
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            var value = Expression.Parameter(typeof(object));
            var method = typeof(RuleCompiler).GetMethod(type.GetGenericTypeDefinition() == typeof(Task<>) ? nameof(AwaitTask) : nameof(AwaitValueTask))!.MakeGenericMethod(type.GetGenericArguments());
            return Expression.Lambda<Func<object?, ValueTask<object?>>>(Expression.Call(method, Expression.Convert(value, type)), value).Compile(_options.ExecutionMode == RuleExecutionMode.Interpreted);
        }
        if (type == typeof(Task)) return async value => { await ((Task)value!).ConfigureAwait(false); return null; };
        if (type == typeof(ValueTask)) return async value => { await ((ValueTask)value!).ConfigureAwait(false); return null; };
        return value => ValueTask.FromResult(value);
    }
    public static async ValueTask<object?> AwaitTask<T>(Task<T> task) => await task.ConfigureAwait(false);
    public static async ValueTask<object?> AwaitValueTask<T>(ValueTask<T> task) => await task.ConfigureAwait(false);
    private static RuleDiagnostic Diagnostic(string code, CompileFailure failure)
    {
        int start = Math.Clamp(failure.Span.Start, 0, code.Length), line = 1, column = 1;
        for (int i = 0; i < start; i++) { if (code[i] == '\r') { line++; column = 1; if (i + 1 < start && code[i + 1] == '\n') i++; } else if (code[i] == '\n') { line++; column = 1; } else column++; }
        return new(failure.Code, failure.Message, new(start, Math.Clamp(failure.Span.Length, 0, code.Length - start)), line, column, failure.Precision);
    }
}
