using System.Linq.Expressions;
using DynamicExpresso;
using DynamicExpresso.Exceptions;
using System.Reflection;

namespace CRules;

/// <summary>唯一表达式解析适配层，只使用上游公开 API，不执行或独立编译片段。</summary>
internal sealed class DynamicExpressoAdapter
{
    private readonly Interpreter _interpreter;

    public const string ResultTypeAlias = "__crules_result_type";
    public string ResultConstructionName { get; }

    public DynamicExpressoAdapter(RuleCompileOptions options, Type resultType)
    {
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
        var parameters = symbols.Select(p => new Parameter(p)).ToArray();
        // 只适配初始化器结构；构造重载、成员值的表达式与隐式转换仍交由上游。
        var initializer = ParseInitializer(text, parameters);
        if (initializer is not null)
        {
            if (expected is null || initializer.Type == expected) return initializer;
            const string binding = "__crules_initialized_value";
            var value = Expression.Parameter(initializer.Type, binding);
            var converted = _interpreter.Parse(binding, expected, parameters.Append(new Parameter(value)).ToArray()).Expression;
            return Expression.Block([value], Expression.Assign(value, initializer), converted);
        }
        return expected is null
            ? _interpreter.Parse(text, parameters).Expression
            : _interpreter.Parse(text, expected, parameters).Expression;
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
        if (!constructorText.EndsWith(')')) constructorText += "()";
        var creation = _interpreter.Parse(constructorText, parameters).Expression as NewExpression;
        if (creation is null) return null; // 数组等其他 new 形式由上游处理。
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
