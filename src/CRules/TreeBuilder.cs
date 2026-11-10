using System.Linq.Expressions;
using DynamicExpresso.Exceptions;
namespace CRules;

internal sealed class TreeBuilder(string text, CompilerOptions options, Type returnType, ParameterExpression[] parameters)
{
    private sealed record Symbol(ParameterExpression Node, bool ReadOnly);
    private readonly LabelTarget _exit = Expression.Label(returnType, "exit");
    private readonly DynamicExpressoAdapter _adapter = new(options, returnType);
    private readonly Stack<Dictionary<string, Symbol>> _scopes = new();
    private int _locals;
    private readonly Stack<LabelTarget> _breakTargets = new();
    private readonly Dictionary<SwitchStatement, SwitchSection?> _constantSelections = new();
    private readonly Dictionary<Conditional, bool> _constantConditions = new();
    private readonly HashSet<SwitchStatement> _exhaustiveSwitches = [];

    public LambdaExpression Build(Block block)
    {
        var compiled = CompileBlock(block);
        if (!Returns(block)) throw new CompileFailure("CRL2005", "所有可达终止路径必须 return。", block.Span, PositionPrecision.Statement);
        var body = Expression.Block(compiled, Expression.Label(_exit, Expression.Default(returnType)));
        return Expression.Lambda(body, parameters);
    }
    [Flags]
    private enum Exits { Next = 1, Return = 2, Break = 4 }
    private bool Returns(Statement statement) => Analyze(statement) == Exits.Return;
    private bool Terminates(Statement statement) => (Analyze(statement) & Exits.Next) == 0;
    private Exits Analyze(Statement statement)
    {
        switch (statement)
        {
            case Return: return Exits.Return;
            case Break: return Exits.Break;
            case Conditional c:
                return _constantConditions.TryGetValue(c, out var condition)
                    ? condition ? Analyze(c.Then) : c.Else is null ? Exits.Next : Analyze(c.Else)
                    : Analyze(c.Then) | (c.Else is null ? Exits.Next : Analyze(c.Else));
            case Block b:
                var exits = Exits.Next;
                foreach (var child in b.Statements)
                {
                    if ((exits & Exits.Next) == 0) break;
                    exits = (exits & ~Exits.Next) | Analyze(child);
                }
                return exits;
            case SwitchStatement s:
                Exits branches;
                if (_constantSelections.TryGetValue(s, out var selected)) branches = selected is null ? Exits.Next : Analyze(selected.Body);
                else
                {
                    branches = s.Sections.Any(x => x.IsDefault) || _exhaustiveSwitches.Contains(s) ? (Exits)0 : Exits.Next;
                    foreach (var section in s.Sections) branches |= Analyze(section.Body);
                }
                // break 仅退出当前 switch，不能被当作函数返回。
                return (branches & ~Exits.Break) | ((branches & Exits.Break) != 0 ? Exits.Next : 0);
            default: return Exits.Next;
        }
    }
    private Expression CompileBlock(Block block)
    {
        _scopes.Push(new(StringComparer.Ordinal));
        List<Expression> expressions = [];
        bool returned = false;
        foreach (var statement in block.Statements)
        {
            if (returned) throw new CompileFailure("CRL2006", "不可达语句。", statement.Span, PositionPrecision.Statement);
            expressions.Add(Compile(statement));
            returned = Terminates(statement);
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
                if (SyntaxNames.IsReserved(d.Name, options.ReferencedTypes)
                    || parameters.Any(p => p.Name == d.Name) || Find(d.Name) is not null)
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
            case CompoundAssignment a:
                var local = Find(a.Name) ?? throw new CompileFailure("CRL2003", "复合赋值只能修改已声明局部变量。", a.Span);
                if (local.ReadOnly) throw new CompileFailure("CRL2004", $"const 不能再次赋值：{a.Name}。", a.Span);
                var rhs = text.Substring(a.Value.Start, a.Value.Length);
                try
                {
                    var nodes = _scopes.SelectMany(s => s.Values).Select(s => s.Node).Concat(parameters);
                    var rightValue = Parse(a.Value);
                    var narrowType = Nullable.GetUnderlyingType(local.Node.Type) ?? local.Node.Type;
                    bool narrow = narrowType == typeof(byte) || narrowType == typeof(sbyte) || narrowType == typeof(short) || narrowType == typeof(ushort) || narrowType == typeof(char);
                    var left = narrow ? $"({(Nullable.GetUnderlyingType(local.Node.Type) is null ? "int" : "int?")}){a.Name}" : a.Name;
                    var rightText = narrow && (Nullable.GetUnderlyingType(rightValue.Type) ?? rightValue.Type) == typeof(char)
                        ? $"({(Nullable.GetUnderlyingType(rightValue.Type) is null ? "int" : "int?")})({rhs})" : rhs;
                    var operation = _adapter.Parse($"{left} {a.Operator} ({rightText})", null, nodes);
                    if (operation.Type != local.Node.Type)
                    {
                        if (!NumericConversions.Implicit(rightValue.Type, local.Node.Type, (rightValue as ConstantExpression)?.Value, rightValue is ConstantExpression))
                            throw new CompileFailure("CRL3001", "复合赋值的右值不能隐式转换为局部变量类型。", a.Span);
                        operation = Expression.Convert(operation, local.Node.Type);
                    }
                    if (options.DecimalOnly) operation = new DynamicCalls().Visit(operation);
                    new ExpressionAudit(options, a.Span).Visit(operation);
                    return Expression.Assign(local.Node, operation);
                }
                catch (ParseException error) { throw new CompileFailure("CRL3001", error.Message, a.Span, PositionPrecision.Statement); }
            case Break b:
                if (_breakTargets.Count == 0) throw new CompileFailure("CRL2010", "break 只能位于 switch 内。", b.Span);
                return Expression.Break(_breakTargets.Peek());
            case SwitchStatement s: return CompileSwitch(s);
            case Conditional c:
                var condition = Parse(c.Condition, typeof(bool));
                if (BooleanConstant(condition) is bool known) _constantConditions[c] = known;
                return Expression.IfThenElse(condition, Compile(c.Then),
                    c.Else is null ? Expression.Empty() : Compile(c.Else));
            case Return r: return Expression.Return(_exit, Parse(r.Value, returnType, allowTargetNew: true));
            default: throw new InvalidOperationException("未知语句节点。");
        }
    }
    private static bool? BooleanConstant(Expression expression) => expression switch
    {
        ConstantExpression { Value: bool value } => value,
        UnaryExpression { NodeType: ExpressionType.Not, Method: null } unary when BooleanConstant(unary.Operand) is bool value => !value,
        BinaryExpression { NodeType: ExpressionType.AndAlso, Method: null } binary when BooleanConstant(binary.Left) is bool left && BooleanConstant(binary.Right) is bool right => left && right,
        BinaryExpression { NodeType: ExpressionType.OrElse, Method: null } binary when BooleanConstant(binary.Left) is bool left && BooleanConstant(binary.Right) is bool right => left || right,
        _ => null
    };
    private Expression CompileSwitch(SwitchStatement statement)
    {
        var value = Parse(statement.Value);
        SwitchSupport.ValidateSelector(value.Type, statement.Value.Start);
        ConstantExpression? constantSelector = null;
        try { constantSelector = SwitchSupport.Constant(value, statement.Value.Start); }
        catch (CompileFailure) { } // 非常量选择值必须保守检查所有路径。
        SwitchSection? selectedSection = null;
        SwitchSection? defaultSection = null;
        var selector = Expression.Variable(value.Type, "__crules_switch_value");
        var end = Expression.Label("__crules_switch_end");
        _breakTargets.Push(end);
        List<(Expression Test, Expression Body)> branches = [];
        HashSet<object?> constants = [];
        Expression fallback = Expression.Empty();
        foreach (var section in statement.Sections)
        {
            var body = CompileBlock(section.Body);
            if (!Terminates(section.Body)) throw new CompileFailure("CRL2010", "switch 分支必须以 return 或 break 结束，不能贯穿。", section.Body.Span);
            Expression? test = null;
            foreach (var label in section.Labels)
            {
                var labelText = text.Substring(label.Start, label.Length);
                var nodes = _scopes.SelectMany(scope => scope.Values).Select(symbol => symbol.Node).Concat(parameters);
                var literal = SwitchSupport.Label(labelText, value.Type, (expression, expected) => _adapter.Parse(expression, expected, nodes), label.Start,
                    tree => new ExpressionAudit(options, new(label.Start, label.Length)).Visit(tree));
                if (constantSelector is not null && Equals(constantSelector.Value, literal.Value)) selectedSection = section;
                if (!constants.Add(literal.Value)) throw new CompileFailure("CRL2010", "重复的 case 常量。", new(label.Start, label.Length));
                var equal = Expression.Equal(selector, literal);
                test = test is null ? equal : Expression.OrElse(test, equal);
            }
            if (section.IsDefault) { fallback = body; defaultSection = section; }
            if (test is not null) branches.Add((test, body));
        }
        _breakTargets.Pop();
        if (constantSelector is not null) _constantSelections[statement] = selectedSection ?? defaultSection;
        if ((Nullable.GetUnderlyingType(value.Type) ?? value.Type) == typeof(bool)
            && constants.Contains(true) && constants.Contains(false)
            && (value.Type == typeof(bool) || constants.Contains(null))) _exhaustiveSwitches.Add(statement);
        for (int i = branches.Count - 1; i >= 0; i--) fallback = Expression.IfThenElse(branches[i].Test, branches[i].Body, fallback);
        return Expression.Block([selector], Expression.Assign(selector, value), fallback, Expression.Label(end));
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
                if (returnType.IsInterface || returnType.IsAbstract || returnType == typeof(object))
                    throw new CompileFailure("CRL2008", "new() 需要明确、可构造的具体结果类型。", new(slice.Start, slice.Length), PositionPrecision.Expression);
                insertion = at;
                injected = (at == 3 ? " " : "") + injected;
                expression = expression.Insert(at, injected);
            }
        }
        try
        {
            var nodes = _scopes.SelectMany(s => s.Values).Select(s => s.Node).Concat(parameters);
            var tree = _adapter.Parse(expression, expected, nodes);
            if (options.DecimalOnly) tree = new DynamicCalls().Visit(tree);
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
            throw new CompileFailure("CRL3001", error.Message.Replace(DynamicExpressoAdapter.ResultTypeAlias, returnType.Name, StringComparison.Ordinal),
                lambda ? new(slice.Start, slice.Length) : new(slice.Start + Math.Clamp(position, 0, slice.Length), 1),
                lambda ? PositionPrecision.Expression : PositionPrecision.Point);
        }
        catch (ArgumentException error)
        {
            throw new CompileFailure("CRL3001", error.Message.Replace(DynamicExpressoAdapter.ResultTypeAlias, returnType.Name, StringComparison.Ordinal),
                new(slice.Start, slice.Length), PositionPrecision.Expression);
        }
    }
}
