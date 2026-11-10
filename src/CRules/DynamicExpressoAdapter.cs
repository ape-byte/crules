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

    public const string ResultTypeAlias = "__crules_result_type";
    public string ResultConstructionName { get; }

    public DynamicExpressoAdapter(CompilerOptions options, Type resultType)
    {
        _options = options;
        _interpreter = new Interpreter(InterpreterOptions.Default | InterpreterOptions.LambdaExpressions)
            .EnableAssignment(AssignmentOperators.None)
            .Reference(typeof(Math)).Reference(typeof(MidpointRounding)).Reference(typeof(Enumerable));
        foreach (var type in options.ReferencedTypes) _interpreter.Reference(type);
        int typeIndex = 0;
        ResultConstructionName = RegisterType(resultType, ResultTypeAlias, ref typeIndex);
    }

    private string RegisterType(Type type, string alias, ref int index)
    {
        if (type.IsArray) return RegisterType(type.GetElementType()!, alias, ref index) + "[]";
        if (!type.IsConstructedGenericType) { _interpreter.Reference(type, alias); return alias; }
        _interpreter.Reference(type.GetGenericTypeDefinition(), alias);
        List<string> arguments = [];
        foreach (var argument in type.GetGenericArguments())
            arguments.Add(RegisterType(argument, "__crules_type_arg_" + index++, ref index));
        return alias + "<" + string.Join(",", arguments) + ">";
    }

    public Expression Parse(string text, Type? expected, IEnumerable<ParameterExpression> symbols)
    {
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
            if (expected is null || expected == array.Type) return array;
            var slot = Expression.Parameter(array.Type, "__crules_array");
            var converted = _interpreter.Parse(slot.Name!, expected, new Parameter(slot)).Expression;
            return Expression.Block([slot], Expression.Assign(slot, array), converted);
        }
        // 只适配初始化器结构；构造重载、成员值的表达式与隐式转换仍交由上游。
        var initializer = ValueCreation(text) ?? ParseInitializer(text, parameters);
        if (initializer is not null)
        {
            if (expected is null || initializer.Type == expected) return initializer;
            const string binding = "__crules_initialized_value";
            var value = Expression.Parameter(initializer.Type, binding);
            var converted = _interpreter.Parse(binding, expected, parameters.Append(new Parameter(value)).ToArray()).Expression;
            return Expression.Block([value], Expression.Assign(value, initializer), converted);
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

    private static int FindBoundary(string text, int start, char delimiter)
    {
        int depth = 0;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\"' or '\'' || c == '@' && i + 1 < text.Length && text[i + 1] == '\"')
            { i = StatementParser.SkipLiteral(text, i) - 1; continue; }
            if (depth == 0 && c == delimiter) return i;
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') { if (depth == 0) return -1; depth--; }
        }
        return -1;
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
