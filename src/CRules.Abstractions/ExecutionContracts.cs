namespace CRules;

public readonly record struct SourceSpan(int Start, int Length);
public enum PositionPrecision { Point, Expression, Statement }
public enum RuleExecutionMode { JitCompiled, Interpreted }
public sealed record RuleDiagnostic(string Code, string Message, SourceSpan Span,
    int Line, int Column, PositionPrecision Precision = PositionPrecision.Point);
public sealed record InputParameter(string Name, Type Type);
public sealed class CompileOptions
{
    public required string Code { get; init; }
    public string? Name { get; init; }
    public required Type ReturnType { get; init; }
    public IReadOnlyList<InputParameter> Parameters { get; init; } = [];
}
public sealed class Compilation<T>(T? executable, IReadOnlyList<RuleDiagnostic> diagnostics) where T : class
{
    public T? Executable { get; } = executable;
    public IReadOnlyList<RuleDiagnostic> Diagnostics { get; } = diagnostics;
    public bool Success => Executable is not null && Diagnostics.Count == 0;
    public T RequireExecutable() => Executable ?? throw new InvalidOperationException(string.Join(Environment.NewLine,
        Diagnostics.Select(d => $"{d.Code} ({d.Line},{d.Column}): {d.Message}")));
}
public class Executable
{
    private readonly Func<object?[], object?> _execute;
    private readonly Func<object?, ValueTask<object?>> _await;
    public string Code { get; }
    public string? Name { get; }
    public Type ReturnType { get; }
    public IReadOnlyList<InputParameter> Parameters { get; }
    public RuleExecutionMode ExecutionMode { get; }
    public Executable(CompileOptions options, RuleExecutionMode mode, Func<object?[], object?> execute,
        Func<object?, ValueTask<object?>> awaitResult)
    {
        Code = options.Code; Name = options.Name; ReturnType = options.ReturnType;
        Parameters = Array.AsReadOnly(options.Parameters.ToArray()); ExecutionMode = mode;
        _execute = execute; _await = awaitResult;
    }
    public object? Execute(params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length != Parameters.Count) throw new ArgumentException("实参数量与形参签名不一致。", nameof(arguments));
        for (int i = 0; i < arguments.Length; i++)
        {
            var t = Parameters[i].Type; var value = arguments[i];
            if (value is null ? t.IsValueType && Nullable.GetUnderlyingType(t) is null : !t.IsInstanceOfType(value))
                throw new ArgumentException($"参数 {Parameters[i].Name} 的类型不匹配。", nameof(arguments));
        }
        return _execute(arguments);
    }
    public async ValueTask<object?> ExecuteAsync(object?[] arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = Execute(arguments);
        return await _await(value).ConfigureAwait(false);
    }
    public ValueTask<object?> ExecuteAsync(params object?[] arguments) => ExecuteAsync(arguments, default);
}
public sealed class Executable<TDelegate>(CompileOptions options, RuleExecutionMode mode, TDelegate execute,
    Func<object?[], object?> invoke, Func<object?, ValueTask<object?>> awaitResult)
    : Executable(options, mode, invoke, awaitResult) where TDelegate : Delegate
{
    public new TDelegate Execute { get; } = execute;
}
public interface IRuleCompiler
{
    Compilation<Executable> Compile(CompileOptions options);
    Compilation<Executable<TDelegate>> Compile<TDelegate>(string code, string[] parameterNames, string? name = null) where TDelegate : Delegate;
    Compilation<Executable<Func<TInput, TResult>>> Compile<TInput, TResult>(string code, string parameterName = "ctx", string? name = null);
}
