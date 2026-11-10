using System.Linq.Expressions;
using DynamicExpresso.Exceptions;
namespace CRules;

internal sealed class TreeBuilder(string text, CompilerOptions options, Type returnType, ParameterExpression[] parameters)
{
    private sealed record Symbol(ParameterExpression Node, bool ReadOnly);
    private readonly LabelTarget _exit = Expression.Label(returnType, "exit");
    private DynamicExpressoAdapter? _expressionAdapter;
    private DynamicExpressoAdapter _adapter => _expressionAdapter ??= new(options, returnType) { OutArgument = ResolveOutArgument };
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
                var declaredType = d.TypeName is Slice typeName ? ResolveLocalType(typeName, d.Value) : null;
                var value = Parse(d.Value, declaredType, allowTargetNew: declaredType is not null);
                if (value.Type == typeof(void) || declaredType is null && value is ConstantExpression { Value: null, Type: var t } && t == typeof(object))
                    throw new CompileFailure("CRL2003", "变量需要可推断的非 void 类型。", d.Span);
                if (++_locals > options.MaxLocals) throw new CompileFailure("CRL4001", "局部变量超过限额。", d.Span);
                var node = Expression.Variable(declaredType ?? value.Type, d.Name);
                _scopes.Peek().Add(d.Name, new(node, d.ReadOnly));
                return Expression.Assign(node, value);
            case Assignment a:
                var symbol = Find(a.Name) ?? throw new CompileFailure("CRL2003", $"未声明变量 {a.Name}。", a.Span);
                if (symbol.ReadOnly) throw new CompileFailure("CRL2004", $"const 不能再次赋值：{a.Name}。", a.Span);
                return Expression.Assign(symbol.Node, Parse(a.Value, symbol.Node.Type));
            case IndexAssignment a:
                if (Parse(a.Target) is not IndexExpression { Object: { } receiver, Indexer.SetMethod: { IsPublic: true } } index
                    || DictionarySupport.Contract(receiver.Type) is null)
                    throw new CompileFailure("CRL2003", "索引赋值需要可写的泛型字典。", a.Span);
                var assignment = a.Operator is null ? Expression.Assign(index, Parse(a.Value, index.Type, allowTargetNew: true))
                    : CompileIndexCompound(index, a);
                new ExpressionAudit(options, a.Span).Visit(assignment);
                return assignment;
            case CallStatement call:
                var invocation = Parse(call.Value);
                if (invocation is not MethodCallExpression)
                    throw new CompileFailure("CRL2001", "独立表达式语句必须是方法调用。", call.Span);
                return Expression.Block(invocation, Expression.Empty());
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
                catch (ParseException error) { throw new CompileFailure("CRL3001", _adapter.DiagnosticMessage(error.Message), a.Span, PositionPrecision.Statement); }
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
    private Expression CompileIndexCompound(IndexExpression index, IndexAssignment statement)
    {
        var receiver = Expression.Variable(index.Object!.Type, "__crules_index_receiver");
        var keys = index.Arguments.Select((a, i) => Expression.Variable(a.Type, "__crules_index_key_" + i)).ToArray();
        var target = Expression.MakeIndex(receiver, index.Indexer, keys);
        var left = Expression.Variable(index.Type, "__crules_index_left");
        var rhs = Parse(statement.Value);
        var right = Expression.Variable(rhs.Type, "__crules_index_right");
        Expression operation;
        try { operation = _adapter.Parse($"{left.Name} {statement.Operator} {right.Name}", null, [left, right]); }
        catch (ParseException error) { throw new CompileFailure("CRL3001", error.Message, statement.Span, PositionPrecision.Statement); }
        if (operation.Type != index.Type)
        {
            if (!NumericConversions.Implicit(rhs.Type, index.Type, (rhs as ConstantExpression)?.Value, rhs is ConstantExpression))
                throw new CompileFailure("CRL3001", "复合赋值右值不能隐式转换为字典值类型。", statement.Span);
            operation = Expression.Convert(operation, index.Type);
        }
        List<Expression> expressions = [Expression.Assign(receiver, index.Object)];
        expressions.AddRange(keys.Select((key, i) => Expression.Assign(key, index.Arguments[i])));
        expressions.Add(Expression.Assign(left, target));
        expressions.Add(Expression.Assign(right, rhs));
        expressions.Add(Expression.Assign(target, operation));
        return Expression.Block(new[] { receiver, left, right }.Concat(keys), expressions);
    }
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
    private ParameterExpression ResolveOutArgument(string name, Type type, bool declare)
    {
        if (name == "_" && (declare || Find(name) is null))
        {
            name = "__crules_discard_" + _locals;
            if (++_locals > options.MaxLocals) throw new ParseException("局部变量超过限额。", 0);
            var discard = Expression.Variable(type, name);
            _scopes.Peek().Add(name, new(discard, false));
            return discard;
        }
        if (declare)
        {
            if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_') || name.Any(c => !char.IsLetterOrDigit(c) && c != '_')
                || SyntaxNames.IsReserved(name, options.ReferencedTypes) || Find(name) is not null || parameters.Any(p => p.Name == name))
                throw new ParseException("out var 名称无效或已占用。", 0);
            if (++_locals > options.MaxLocals) throw new ParseException("局部变量超过限额。", 0);
            var node = Expression.Variable(type, name);
            _scopes.Peek().Add(name, new(node, false));
            return node;
        }
        var local = Find(name);
        if (local is null || local.ReadOnly || local.Node.Type != type)
            throw new ParseException("out 参数需要类型完全匹配的可写局部变量；不能修改形参。", 0);
        return local.Node;
    }
    private Type ResolveLocalType(Slice name, Slice value)
    {
        try
        {
            string typeName = text.Substring(name.Start, name.Length).Trim();
            var type = typeName == "List"
                ? _adapter.InferListType(text.Substring(value.Start, value.Length),
                    _scopes.SelectMany(s => s.Values).Select(s => s.Node).Concat(parameters))
                : _adapter.ResolveType(typeName);
            if (type == typeof(void) || type.IsByRef || RuntimeCompatibility.IsByRefLike(type) || type.IsPointer || type.ContainsGenericParameters)
                throw new ParseException("不支持的局部变量类型。", 0);
            return type;
        }
        catch (Exception error) when (error is ParseException or ArgumentException)
        { throw new CompileFailure("CRL3001", _adapter.DiagnosticMessage(error.Message), new(name.Start, name.Length), PositionPrecision.Expression); }
    }
    private Expression Parse(Slice slice, Type? expected = null, bool allowTargetNew = false)
    {
        string expression = text.Substring(slice.Start, slice.Length);
        int insertion = -1;
        string injected = "";
        if (allowTargetNew && expression.StartsWith("new", StringComparison.Ordinal))
        {
            int at = 3;
            while (at < expression.Length && char.IsWhiteSpace(expression[at])) at++;
            if (at < expression.Length && expression[at] == '(')
            {
                var target = expected!;
                if (target.IsInterface || target.IsAbstract || target == typeof(object))
                    throw new CompileFailure("CRL2008", "new() 需要明确、可构造的具体结果类型。", new(slice.Start, slice.Length), PositionPrecision.Expression);
                insertion = at;
                injected = _adapter.ConstructionName(target);
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
            // 嵌套 Lambda 和 out 重写的位置可能来自子表达式，诚实降级为整个片段。
            bool lambda = expression.Contains("=>", StringComparison.Ordinal) || expression.Contains("out", StringComparison.Ordinal);
            int position = error.Position;
            if (insertion >= 0 && position >= insertion)
                position = position < insertion + injected.Length ? insertion : position - injected.Length;
            throw new CompileFailure("CRL3001", _adapter.DiagnosticMessage(error.Message),
                lambda ? new(slice.Start, slice.Length) : new(slice.Start + RuntimeCompatibility.Clamp(position, 0, slice.Length), 1),
                lambda ? PositionPrecision.Expression : PositionPrecision.Point);
        }
        catch (ArgumentException error)
        {
            throw new CompileFailure("CRL3001", _adapter.DiagnosticMessage(error.Message),
                new(slice.Start, slice.Length), PositionPrecision.Expression);
        }
    }
}
