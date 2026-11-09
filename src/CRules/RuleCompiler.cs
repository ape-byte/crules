using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;
using DynamicExpresso.Exceptions;

namespace CRules;

/// <summary>有限语句编译器。类型推断、转换、运算符与方法解析均委托公开 Parse API。</summary>
public sealed class RuleCompiler : IRuleCompiler
{
    private readonly RuleCompileOptions _options;

    public RuleCompiler(RuleCompileOptions? options = null)
    {
        var input = options ?? new();
        _options = input with { ReferencedTypes = input.ReferencedTypes.ToArray() };
        if (!Enum.IsDefined(_options.ExecutionMode)) throw new ArgumentOutOfRangeException(nameof(options), "执行模式无效。");
        if (_options.MaxSourceLength <= 0 || _options.MaxStatements <= 0 || _options.MaxDepth <= 0
            || _options.MaxExpressionLength <= 0 || _options.MaxTreeNodes <= 0 || _options.MaxLocals <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "资源限额必须为正数。");
    }

    public RuleCompilation<TContext, TResult> Compile<TContext, TResult>(RuleSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Text);
        try
        {
            if (_options.ExecutionMode == RuleExecutionMode.JitCompiled && !RuntimeFeature.IsDynamicCodeSupported)
                throw new CompileFailure("CRL4004", "当前运行时不支持动态代码；NativeAOT 请显式选择 Interpreted 模式或使用预编译原生规则。", new(0, 0));
            if (source.Text.Length > _options.MaxSourceLength)
                throw new CompileFailure("CRL4001", "源码长度超过限额。", new(0, source.Text.Length));
            if (typeof(Delegate).IsAssignableFrom(typeof(TResult)) || typeof(Task).IsAssignableFrom(typeof(TResult))
                || typeof(Expression).IsAssignableFrom(typeof(TResult))
                || typeof(System.Collections.IEnumerable).IsAssignableFrom(typeof(TResult)) && typeof(TResult) != typeof(string))
                throw new CompileFailure("CRL2007", "不支持委托、任务或惰性集合返回值。", new(0, 0));
            var syntax = new StatementParser(source.Text, _options);
            var block = syntax.Parse();
            var builder = new TreeBuilder<TContext, TResult>(syntax.Text, _options);
            var tree = builder.Build(block);
            new ExpressionAudit(_options, new(0, source.Text.Length), allowRootLambda: true).Visit(tree);
            // 仅此处编译一次，片段不会单独生成委托。
            var execute = tree.Compile(preferInterpretation: _options.ExecutionMode == RuleExecutionMode.Interpreted);
            var fingerprint = Fingerprint<TContext, TResult>(source.Text);
            return new(new(source, fingerprint, execute, _options.ExecutionMode), []);
        }
        catch (CompileFailure failure)
        {
            int start = Math.Clamp(failure.Span.Start, 0, source.Text.Length);
            var span = new SourceSpan(start, Math.Clamp(failure.Span.Length, 0, source.Text.Length - start));
            int line = 1, column = 1;
            for (int i = 0; i < start; i++)
            {
                if (source.Text[i] == '\r') { line++; column = 1; if (i + 1 < start && source.Text[i + 1] == '\n') i++; }
                else if (source.Text[i] == '\n') { line++; column = 1; }
                else column++;
            }
            return new(null, [new(failure.Code, failure.Message, span, line, column, failure.Precision)]);
        }
    }

    private string Fingerprint<TContext, TResult>(string text)
    {
        // 宿主 callback 的语义由 PolicyVersion 标识；本阶段不提供共享编译缓存。
        // 长度前缀的二进制编码不依赖反射 JSON，在 NativeAOT 中同样确定。
        using var identity = new MemoryStream();
        using(var writer = new BinaryWriter(identity, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("crules-v3-typed-metadata-new"); writer.Write(text);
            writer.Write(typeof(DynamicExpresso.Interpreter).Assembly.FullName ?? "");
            writer.Write(typeof(TContext).AssemblyQualifiedName ?? "");
            writer.Write(typeof(TResult).AssemblyQualifiedName ?? "");
            writer.Write(_options.DecimalOnly);writer.Write(_options.AllowRuntimeLambdaBinding);
            writer.Write((int)_options.ExecutionMode);writer.Write(_options.PolicyVersion);
            writer.Write(_options.ReferencedTypes.Count);
            foreach(var type in _options.ReferencedTypes) writer.Write(type.AssemblyQualifiedName ?? "");
        }
        return Convert.ToHexString(SHA256.HashData(identity.GetBuffer().AsSpan(0, checked((int)identity.Length))));
    }
}

internal sealed class TreeBuilder<TContext, TResult>(string text, RuleCompileOptions options)
{
    private sealed record Symbol(ParameterExpression Node, bool ReadOnly);
    private readonly ParameterExpression _ctx = Expression.Parameter(typeof(TContext), "ctx");
    private readonly LabelTarget _exit = Expression.Label(typeof(TResult), "exit");
    private readonly DynamicExpressoAdapter _adapter = new(options, typeof(TResult));
    private readonly Stack<Dictionary<string, Symbol>> _scopes = new();
    private int _locals;

    public Expression<Func<TContext, TResult>> Build(Block block)
    {
        if (!Returns(block)) throw new CompileFailure("CRL2005", "所有可达终止路径必须 return。", block.Span, PositionPrecision.Statement);
        var body = Expression.Block(CompileBlock(block), Expression.Label(_exit, Expression.Default(typeof(TResult))));
        return Expression.Lambda<Func<TContext, TResult>>(body, _ctx);
    }
    private static bool Returns(Statement statement) => statement switch
    {
        Return => true,
        Block b => b.Statements.Any(Returns),
        Conditional c => c.Else is not null && Returns(c.Then) && Returns(c.Else),
        _ => false
    };
    private Expression CompileBlock(Block block)
    {
        _scopes.Push(new(StringComparer.Ordinal));
        List<Expression> expressions = [];
        bool returned = false;
        foreach (var statement in block.Statements)
        {
            if (returned) throw new CompileFailure("CRL2006", "不可达语句。", statement.Span, PositionPrecision.Statement);
            expressions.Add(Compile(statement));
            returned = Returns(statement);
        }
        expressions.Add(Expression.Empty());
        var locals = _scopes.Pop().Values.Select(s => s.Node).ToArray();
        return Expression.Block(locals, expressions);
    }
    private Expression Compile(Statement statement)
    {
        switch (statement)
        {
            case Block block: return CompileBlock(block);
            case Declaration d:
                if (d.Name is "ctx" or "var" or "const" or "if" or "else" or "return" or DynamicExpressoAdapter.ResultTypeAlias
                    || d.Name.StartsWith("__crules_", StringComparison.Ordinal)
                    || Find(d.Name) is not null || options.ReferencedTypes.Any(t => t.Name == d.Name)
                    || d.Name is "Math" or "Enumerable" or "MidpointRounding")
                    throw new CompileFailure("CRL2002", $"名称已占用或保留：{d.Name}。", d.Span);
                var value = Parse(d.Value);
                if (value.Type == typeof(void) || value is ConstantExpression { Value: null, Type: var t } && t == typeof(object))
                    throw new CompileFailure("CRL2003", "变量需要可推断的非 void 类型。", d.Span);
                if (++_locals > options.MaxLocals) throw new CompileFailure("CRL4001", "局部变量超过限额。", d.Span);
                var node = Expression.Variable(value.Type, d.Name);
                _scopes.Peek().Add(d.Name, new(node, d.ReadOnly));
                return Expression.Assign(node, value);
            case Assignment a:
                var symbol = Find(a.Name) ?? throw new CompileFailure("CRL2003", $"未声明变量 {a.Name}。", a.Span);
                if (symbol.ReadOnly) throw new CompileFailure("CRL2004", $"const 不能再次赋值：{a.Name}。", a.Span);
                return Expression.Assign(symbol.Node, Parse(a.Value, symbol.Node.Type));
            case Conditional c:
                return Expression.IfThenElse(Parse(c.Condition, typeof(bool)), CompileBlock(c.Then),
                    c.Else is null ? Expression.Empty() : Compile(c.Else));
            case Return r: return Expression.Return(_exit, Parse(r.Value, typeof(TResult), allowTargetNew: true));
            default: throw new InvalidOperationException("未知语句节点。");
        }
    }
    private Symbol? Find(string name)
    {
        foreach (var scope in _scopes) if (scope.TryGetValue(name, out var symbol)) return symbol;
        return null;
    }
    private Expression Parse(Slice slice, Type? expected = null, bool allowTargetNew = false)
    {
        string expression = text.Substring(slice.Start, slice.Length);
        int insertion = -1;
        string injected = _adapter.ResultConstructionName;
        if (allowTargetNew && expression.StartsWith("new", StringComparison.Ordinal))
        {
            int at = 3;
            while (at < expression.Length && char.IsWhiteSpace(expression[at])) at++;
            if (at < expression.Length && expression[at] == '(')
            {
                if (typeof(TResult).IsInterface || typeof(TResult).IsAbstract || typeof(TResult) == typeof(object))
                    throw new CompileFailure("CRL2008", "new() 需要明确、可构造的具体结果类型。", new(slice.Start, slice.Length), PositionPrecision.Expression);
                insertion = at;
                injected = (at == 3 ? " " : "") + injected;
                expression = expression.Insert(at, injected);
            }
        }
        try
        {
            var nodes = _scopes.SelectMany(s => s.Values).Select(s => s.Node).Prepend(_ctx);
            var tree = _adapter.Parse(expression, expected, nodes);
            new ExpressionAudit(options, new(slice.Start, slice.Length)).Visit(tree);
            return tree;
        }
        catch (ParseException error)
        {
            // 嵌套 Lambda 的 Position 可能是子解析器局部值，诚实降级为整个片段。
            bool lambda = expression.Contains("=>", StringComparison.Ordinal);
            int position = error.Position;
            if (insertion >= 0 && position >= insertion)
                position = position < insertion + injected.Length ? insertion : position - injected.Length;
            throw new CompileFailure("CRL3001", error.Message.Replace(DynamicExpressoAdapter.ResultTypeAlias, typeof(TResult).Name, StringComparison.Ordinal),
                lambda ? new(slice.Start, slice.Length) : new(slice.Start + Math.Clamp(position, 0, slice.Length), 1),
                lambda ? PositionPrecision.Expression : PositionPrecision.Point);
        }
        catch (ArgumentException error)
        {
            throw new CompileFailure("CRL3001", error.Message.Replace(DynamicExpressoAdapter.ResultTypeAlias, typeof(TResult).Name, StringComparison.Ordinal),
                new(slice.Start, slice.Length), PositionPrecision.Expression);
        }
    }
}
