using System.Linq.Expressions;
using DynamicExpresso;
using DynamicExpresso.Exceptions;
using System.Reflection;

namespace CRules;

/// <summary>唯一表达式解析适配层，只使用上游公开 API，不执行或独立编译片段。</summary>
internal sealed class DynamicExpressoAdapter
{
    private readonly Interpreter _interpreter;
    private readonly CompilerOptions _options;
    private readonly Dictionary<Type, string> _constructionNames = new();
    private readonly Dictionary<string, string> _typeLabels = new(StringComparer.Ordinal);
    private int _typeIndex;

    private const string ResultTypeAlias = "__crules_result_type";

    public DynamicExpressoAdapter(CompilerOptions options, Type resultType)
    {
        _options = options;
        _interpreter = new Interpreter(InterpreterOptions.Default | InterpreterOptions.LambdaExpressions)
            .EnableAssignment(AssignmentOperators.None)
            .Reference(typeof(Math)).Reference(typeof(MidpointRounding)).Reference(typeof(Enumerable));
        foreach (var type in options.ReferencedTypes) _interpreter.Reference(type);
        _constructionNames[resultType] = RegisterType(resultType, ResultTypeAlias, ref _typeIndex);
    }

    public Type ResolveType(string name)
    {
        name = name.Trim();
        // 引用类型的 ? 是注解，运行时类型不变；值类型的 ? 构造 Nullable<T>。
        if (name.EndsWith('?'))
        {
            string underlying = name[..^1].TrimEnd();
            if (underlying.EndsWith('?')) throw new ParseException("不能重复使用可空类型后缀。", 0);
            var type = ResolveType(underlying);
            if (Nullable.GetUnderlyingType(type) is not null) throw new ParseException("不能重复使用可空类型后缀。", 0);
            return type.IsValueType ? typeof(Nullable<>).MakeGenericType(type) : type;
        }
        return (_interpreter.Parse($"typeof({name})").Expression as ConstantExpression)?.Value as Type
            ?? throw new ParseException($"未知类型：{name}。", 0);
    }

    public string ConstructionName(Type type)
    {
        if (!_constructionNames.TryGetValue(type, out var name))
        {
            var alias = "__crules_local_type_" + _typeIndex++;
            _constructionNames[type] = name = RegisterType(type, alias, ref _typeIndex);
        }
        return name;
    }

    public Type InferListType(string text, IEnumerable<ParameterExpression> symbols)
    {
        if (!_options.ReferencedTypes.Contains(typeof(List<>)))
            throw new ParseException("List 简写需要在 ReferencedTypes 注册 typeof(List<>)。", 0);
        int open = FindBoundary(text, 0, '{');
        if (open < 0 || string.Concat(text[..open].Where(c => !char.IsWhiteSpace(c))) != "new()"
            || FindClosingBrace(text, open) != text.Length - 1)
            throw new ParseException("List 简写需要 new() { 元素 }；其他形式请显式写出 List<T>。", 0);
        string values = text[(open + 1)..^1];
        var array = ArraySupport.Parse("new[] {" + values + "}", (value, target) => Parse(value, target, symbols), ResolveType,
            (value, target) => _interpreter.Parse("__crules_choose ? __crules_source : __crules_target",
                new Parameter(Expression.Parameter(typeof(bool), "__crules_choose")), new Parameter(Expression.Parameter(value.Type, "__crules_source")),
                new Parameter(Expression.Parameter(target, "__crules_target"))).Expression)!;
        return typeof(List<>).MakeGenericType(array.Type.GetElementType()!);
    }

    internal Func<string, Type, bool, ParameterExpression>? OutArgument { get; init; }

    private Expression? ParseOutCalls(string text, Type? expected, ParameterExpression[] symbols)
    {
        // 只扩展字典的 out 调用；用占位 bool 保留外层短路与三元表达式的惰性求值。
        var calls = new List<(ParameterExpression Slot, Expression Value)>();
        var names = symbols.ToList();
        for (int at = 0; at < text.Length; at++)
        {
            if (text[at] is '\"' or '\'' || text[at] == '@' && at + 1 < text.Length && text[at + 1] == '\"')
            { at = StatementParser.SkipLiteral(text, at) - 1; continue; }
            if (text[at] != '.') continue;
            int method = at + 1;
            while (method < text.Length && char.IsWhiteSpace(text[method])) method++;
            string methodName;
            if (text.AsSpan(method).StartsWith("TryGetValue", StringComparison.Ordinal)) methodName = "TryGetValue";
            else if (text.AsSpan(method).StartsWith("Remove", StringComparison.Ordinal)) methodName = "Remove";
            else continue;
            int open = method + methodName.Length;
            while (open < text.Length && char.IsWhiteSpace(text[open])) open++;
            if (open >= text.Length || text[open] != '(') continue;
            int end = FindBoundary(text, open + 1, ')');
            if (end < 0) continue;
            var args = ArraySupport.Items(text[(open + 1)..end]);
            if (args.Count != 2 || args[1].Length < 4 || !args[1].StartsWith("out", StringComparison.Ordinal) || !char.IsWhiteSpace(args[1][3])) continue;
            int start = ReceiverStart(text, at);
            var receiver = Parse(text[start..at], null, names);
            var contract = DictionarySupport.Contract(receiver.Type) ?? throw new ParseException("out 调用需要泛型字典。", at);
            var types = contract.GetGenericArguments();
            string name = args[1][3..].Trim();
            bool declare = name.Length > 3 && name.StartsWith("var", StringComparison.Ordinal) && char.IsWhiteSpace(name[3]);
            if (declare) name = name[4..].Trim();
            if (declare && HasConditionalOperator(text))
                throw new ParseException("短路或条件表达式中的 out var 暂不支持；请先初始化局部变量，再使用 out 变量。", at);
            var target = OutArgument?.Invoke(name, types[1], declare) ?? throw new ParseException("out 参数需要可写局部变量。", at);
            if (!names.Contains(target)) names.Add(target);
            var key = Parse(args[0], types[0], names);
            var methodInfo = receiver.Type.GetMethod(methodName, [types[0], types[1].MakeByRefType()])
                ?? contract.GetMethod(methodName, [types[0], types[1].MakeByRefType()]);
            if (methodInfo is null) throw new ParseException("该字典契约不支持此 out 方法。", at);
            var call = Expression.Call(receiver, methodInfo, key, target);
            var slot = Expression.Parameter(typeof(bool), "__crules_out_call_" + calls.Count);
            calls.Add((slot, call)); names.Add(slot);
            text = text[..start] + slot.Name + text[(end + 1)..];
            at = start + slot.Name!.Length - 1;
        }
        if (calls.Count == 0) return null;
        var body = Parse(text, expected, names);
        return new OutCalls(calls.ToDictionary(c => c.Slot, c => c.Value)).Visit(body);
    }

    private static int ReceiverStart(string text, int dot)
    {
        var masked = text.ToCharArray();
        for (int at = 0; at < dot; at++)
            if (text[at] is '\"' or '\'' || text[at] == '@' && at + 1 < dot && text[at + 1] == '\"')
            {
                int end = StatementParser.SkipLiteral(text, at);
                Array.Fill(masked, ' ', at, end - at); at = end - 1;
            }
        int depth = 0, start = dot - 1;
        for (; start >= 0; start--)
        {
            char c = masked[start];
            if (c is ')' or ']') depth++;
            else if (c is '(' or '[') { if (depth == 0) break; depth--; }
            else if (depth == 0 && !(char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c is '_' or '.')) break;
        }
        return start + 1;
    }

    private static bool HasConditionalOperator(string text)
    {
        for (int at = 0; at < text.Length; at++)
        {
            if (text[at] is '\"' or '\'' || text[at] == '@' && at + 1 < text.Length && text[at + 1] == '\"')
            { at = StatementParser.SkipLiteral(text, at) - 1; continue; }
            if (text[at] == '?' || text.AsSpan(at).StartsWith("&&", StringComparison.Ordinal) || text.AsSpan(at).StartsWith("||", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private sealed class OutCalls(Dictionary<ParameterExpression, Expression> calls) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => calls.GetValueOrDefault(node, node);
    }

    private string RegisterType(Type type, string alias, ref int index)
    {
        if (type.IsArray) return RegisterType(type.GetElementType()!, alias, ref index) + "[]";
        _typeLabels[alias] = type.Name.Split('`')[0];
        if (!type.IsConstructedGenericType) { _interpreter.Reference(type, alias); return alias; }
        _interpreter.Reference(type.GetGenericTypeDefinition(), alias);
        List<string> arguments = [];
        foreach (var argument in type.GetGenericArguments())
            arguments.Add(RegisterType(argument, "__crules_type_arg_" + index++, ref index));
        return alias + "<" + string.Join(",", arguments) + ">";
    }

    public string DiagnosticMessage(string message)
    {
        foreach (var (alias, label) in _typeLabels.OrderByDescending(pair => pair.Key.Length))
            message = message.Replace(alias, label, StringComparison.Ordinal);
        return message;
    }

    public Expression Parse(string text, Type? expected, IEnumerable<ParameterExpression> symbols)
    {
        symbols = symbols.ToArray();
        var outCall = ParseOutCalls(text, expected, symbols.ToArray());
        if (outCall is not null) return outCall;
        // 容器元素与成员值同样拥有目标类型，支持嵌套 new()。
        int newAt = text.StartsWith("new", StringComparison.Ordinal) ? 3 : -1;
        if (newAt >= 0 && expected is not null)
        {
            while (newAt < text.Length && char.IsWhiteSpace(text[newAt])) newAt++;
            if (newAt < text.Length && text[newAt] == '(')
            {
                if (expected.IsInterface || expected.IsAbstract || expected == typeof(object))
                    throw new ParseException("new() 需要明确、可构造的具体类型。", 0);
                string injected = " " + ConstructionName(expected);
                try { return Parse(text.Insert(newAt, injected), expected, symbols); }
                catch (ParseException error)
                {
                    int position = error.Position < newAt ? error.Position : Math.Max(newAt, error.Position - injected.Length);
                    throw new ParseException(DiagnosticMessage(error.Message), position);
                }
            }
        }
        Expression? switchExpression;
        try
        {
            switchExpression = SwitchSupport.Parse(text, expected, (value, target) => Parse(value, target, symbols),
                tree => new ExpressionAudit(_options, new(0, text.Length)).Visit(tree));
        }
        catch (CompileFailure failure) { throw new ParseException(failure.Message, failure.Span.Start); }
        if (switchExpression is not null) return switchExpression;
        var parameters = symbols.Select(p => new Parameter(p)).ToArray();
        var array = ArraySupport.Parse(text, (value, target) => Parse(value, target, symbols),
            name => (_interpreter.Parse($"typeof({name})").Expression as ConstantExpression)?.Value as Type,
            (value, target) => _interpreter.Parse("__crules_choose ? __crules_source : __crules_target",
                new Parameter(Expression.Parameter(typeof(bool), "__crules_choose")),
                new Parameter(Expression.Parameter(value.Type, "__crules_source")),
                new Parameter(Expression.Parameter(target, "__crules_target"))).Expression);
        if (array is not null)
        {
            return expected is null ? array : ExpressionCompatibility.ConvertImplicit(_interpreter, array, expected);
        }
        // 只适配初始化器结构；构造重载、成员值的表达式与隐式转换仍交由上游。
        var initializer = ValueCreation(text) ?? ParseInitializer(text, parameters);
        if (initializer is not null)
        {
            return expected is null ? initializer : ExpressionCompatibility.ConvertImplicit(_interpreter, initializer, expected);
        }
        return ExpressionCompatibility.Parse(_interpreter, text, expected, parameters);
    }

    private Expression? ParseInitializer(string text, Parameter[] parameters)
    {
        if (!text.StartsWith("new ", StringComparison.Ordinal) && !text.StartsWith("new\t", StringComparison.Ordinal)
            && !text.StartsWith("new\n", StringComparison.Ordinal) && !text.StartsWith("new\r", StringComparison.Ordinal)) return null;
        int open = FindBoundary(text, 0, '{');
        if (open < 0) return null;
        int close = FindClosingBrace(text, open);
        if (close < 0 || !string.IsNullOrWhiteSpace(text[(close + 1)..])) return null;
        string constructorText = text[..open].TrimEnd();
        if (constructorText.EndsWith(']')) return null;
        if (!constructorText.EndsWith(')')) constructorText += "()";
        var creation = ValueCreation(constructorText) ?? _interpreter.Parse(constructorText, parameters).Expression as NewExpression;
        if (creation is null) return null; // 数组等其他 new 形式由上游处理。
        var dictionary = DictionarySupport.Initialize(creation, text[(open + 1)..close],
            (value, target) => Parse(value.Trim(), target, parameters.Select(p => p.Expression).OfType<ParameterExpression>()));
        if (dictionary is not null) return dictionary;
        if (creation.Type.IsGenericType && creation.Type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elementType = creation.Type.GetGenericArguments()[0];
            var values = ArraySupport.Items(text[(open + 1)..close]).Select(value =>
            {
                // List<T> 的单实参加花括号形式与普通元素初始化语义相同。
                if (value.StartsWith('{') && value.EndsWith('}')) value = value[1..^1].Trim();
                return Expression.ElementInit(creation.Type.GetMethod("Add")!, Parse(value, elementType,
                    parameters.Select(p => p.Expression).OfType<ParameterExpression>()));
            });
            return Expression.ListInit(creation, values);
        }
        List<MemberBinding> bindings = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        int at = open + 1;
        while (at < close)
        {
            while (at < close && char.IsWhiteSpace(text[at])) at++;
            if (at == close) break;
            int start = at;
            if (!(char.IsLetter(text[at]) || text[at] == '_')) return null; // 集合初始化保持上游语义。
            while (at < close && (char.IsLetterOrDigit(text[at]) || text[at] == '_')) at++;
            string name = text[start..at];
            while (at < close && char.IsWhiteSpace(text[at])) at++;
            if (at == close || text[at] != '=' || at + 1 < close && text[at + 1] == '=') return null;
            if (!names.Add(name)) throw new ParseException($"初始化成员重复：{name}。", start);
            var members = creation.Type.GetMember(name, BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m is PropertyInfo or FieldInfo).ToArray();
            if (members.Length != 1) throw new ParseException($"未知或不明确的初始化成员：{name}。", start);
            var member = members[0];
            Type memberType;
            if (member is PropertyInfo property)
            {
                if (property.SetMethod is not { IsPublic: true } || property.GetIndexParameters().Length != 0)
                    throw new ParseException($"初始化成员不可写：{name}。", start);
                memberType = property.PropertyType;
            }
            else
            {
                var field = (FieldInfo)member;
                if (field.IsInitOnly || field.IsLiteral) throw new ParseException($"初始化字段不可写：{name}。", start);
                memberType = field.FieldType;
            }
            int valueStart = ++at;
            while (valueStart < close && char.IsWhiteSpace(text[valueStart])) valueStart++;
            int comma = FindBoundary(text, valueStart, ',');
            int end = comma < 0 ? close : comma;
            var valueText = text[valueStart..end];
            Expression value;
            try { value = Parse(valueText, memberType, parameters.Select(p => p.Expression).OfType<ParameterExpression>()); }
            catch (ParseException error) { throw new ParseException(error.Message, valueStart + error.Position); }
            catch (ArgumentException error) { throw new ParseException(error.Message, valueStart); }
            bindings.Add(Expression.Bind(member, value));
            at = end + 1;
        }
        return Expression.MemberInit(creation, bindings);
    }

    private NewExpression? ValueCreation(string text)
    {
        if (!text.StartsWith("new", StringComparison.Ordinal) || text.Length < 6 || !char.IsWhiteSpace(text[3]) || !text.TrimEnd().EndsWith("()", StringComparison.Ordinal)) return null;
        string typeName = text[3..text.LastIndexOf('(')].Trim();
        try
        {
            var type = (_interpreter.Parse($"typeof({typeName})").Expression as ConstantExpression)?.Value as Type;
            return type?.IsValueType == true ? Expression.New(type) : null;
        }
        catch (ParseException) { return null; }
    }

    internal static int FindBoundary(string text, int start, char delimiter)
    {
        int depth = 0, genericDepth = 0;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\"' or '\'' || c == '@' && i + 1 < text.Length && text[i + 1] == '\"')
            { i = StatementParser.SkipLiteral(text, i) - 1; continue; }
            if (c == '<' && (genericDepth > 0 || NewTypeBefore(text, i))) { genericDepth++; continue; }
            if (c == '>' && genericDepth > 0) { genericDepth--; continue; }
            if (genericDepth > 0) continue;
            if (depth == 0 && c == delimiter) return i;
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') { if (depth == 0) return -1; depth--; }
        }
        return -1;
    }

    private static bool NewTypeBefore(string text, int at)
    {
        int keyword = text.LastIndexOf("new", at, StringComparison.Ordinal);
        return keyword >= 0 && keyword + 3 < at && char.IsWhiteSpace(text[keyword + 3])
            && text.AsSpan(keyword + 3, at - keyword - 3).Trim().ToString().All(c => char.IsLetterOrDigit(c) || c is '_' or '.' || char.IsWhiteSpace(c));
    }
    private static int FindClosingBrace(string text, int open)
    {
        int depth = 1;
        for (int i = open + 1; i < text.Length; i++)
        {
            if (text[i] is '\"' or '\'' || text[i] == '@' && i + 1 < text.Length && text[i + 1] == '\"')
            { i = StatementParser.SkipLiteral(text, i) - 1; continue; }
            if (text[i] == '{') depth++;
            if (text[i] == '}') depth--;
            if (depth == 0) return i;
        }
        return -1;
    }
}
