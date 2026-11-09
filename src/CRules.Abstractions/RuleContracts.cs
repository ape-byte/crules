namespace CRules;

/// <summary>源码位置采用零起始 UTF-16 半开区间。</summary>
public readonly record struct SourceSpan(int Start, int Length);

public enum PositionPrecision { Point, Expression, Statement }
public enum RuleExecutionMode { JitCompiled, Interpreted }

public sealed record RuleDiagnostic(string Code, string Message, SourceSpan Span,
    int Line, int Column, PositionPrecision Precision = PositionPrecision.Point);

public sealed record RuleSource(string RuleId, string Revision, string Text);

public sealed class RuleCompilation<TContext, TResult>(CompiledRule<TContext, TResult>? rule,
    IReadOnlyList<RuleDiagnostic> diagnostics)
{
    public bool Success => Rule is not null && Diagnostics.Count == 0;
    public CompiledRule<TContext, TResult>? Rule { get; } = rule;
    public IReadOnlyList<RuleDiagnostic> Diagnostics { get; } = diagnostics;

    public CompiledRule<TContext, TResult> RequireRule() => Rule ??
        throw new InvalidOperationException(string.Join(Environment.NewLine,
            Diagnostics.Select(d => $"{d.Code} ({d.Line},{d.Column}): {d.Message}")));
}

/// <summary>已完成编译的不可变规则。Execute 直接调用单个强类型委托。</summary>
public sealed class CompiledRule<TContext, TResult>(RuleSource source, string fingerprint,
    Func<TContext, TResult> execute, RuleExecutionMode executionMode = RuleExecutionMode.JitCompiled)
{
    public RuleSource Source { get; } = source;
    public string Fingerprint { get; } = fingerprint;
    public Func<TContext, TResult> Execute { get; } = execute;
    public RuleExecutionMode ExecutionMode { get; } = executionMode;
}

public interface IRuleCompiler
{
    RuleCompilation<TContext, TResult> Compile<TContext, TResult>(RuleSource source);
}
