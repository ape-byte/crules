using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using DynamicExpresso.Exceptions;

namespace CRules;

internal static class SwitchSupport
{
    internal static void ValidateSelector(Type type, int position)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(bool) || type == typeof(char)
            || type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)) return;
        throw new CompileFailure("CRL2011", "switch 1.0 的选择值需要字符串、整数、枚举、布尔或 decimal 类型。", new(position, 1));
    }
    internal static ConstantExpression Constant(Expression expression, int position)
    {
        object? value;
        if (expression is ConstantExpression c) return c;
        if (expression is MemberExpression { Member: FieldInfo { IsLiteral: true } field })
        {
            value = field.GetRawConstantValue();
            if (field.FieldType.IsEnum) value = Enum.ToObject(field.FieldType, value!);
            return Expression.Constant(value, field.FieldType);
        }
        if (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
        {
            var source = Constant(convert.Operand, position);
            var type = Nullable.GetUnderlyingType(convert.Type) ?? convert.Type;
            value = source.Value is null ? null : type.IsEnum ? Enum.ToObject(type, source.Value) : Convert.ChangeType(source.Value, type, CultureInfo.InvariantCulture);
            return Expression.Constant(value, convert.Type);
        }
        throw new CompileFailure("CRL2011", "switch 1.0 仅支持字面常量和枚举常量，不支持模式、when 或运行时 case。", new(position, 1));
    }

    internal static ConstantExpression Label(string text, Type target, Func<string, Type?, Expression> parse, int position, Action<Expression>? audit = null)
    {
        try
        {
            var raw = parse(text, null);
            audit?.Invoke(raw);
            var literal = Constant(raw, position);
            if (!NumericConversions.Implicit(literal.Type, target, literal.Value, true))
                throw new CompileFailure("CRL2011", "case 常量不能隐式转换为选择值类型。", new(position, text.Length));
            var type = Nullable.GetUnderlyingType(target) ?? target;
            object? value = literal.Value;
            if (value is not null && literal.Type != type)
                value = type.IsEnum ? Enum.ToObject(type, value) : Convert.ChangeType(value is char ch ? (object)(ushort)ch : value, type, CultureInfo.InvariantCulture);
            return Expression.Constant(value, target);
        }
        catch (Exception error) when (error is OverflowException or InvalidCastException or ArgumentException)
        { throw new CompileFailure("CRL2011", "case 常量类型无效或超出范围。", new(position, text.Length)); }
    }

    internal static Expression? Parse(string text, Type? expected, Func<string, Type?, Expression> parse, Action<Expression>? audit = null)
    {
        int keyword = Find(text, 0, "switch");
        if (keyword < 0) return null;
        int open = keyword + 6;
        while (open < text.Length && char.IsWhiteSpace(text[open])) open++;
        if (open >= text.Length || text[open] != '{' || !text.TrimEnd().EndsWith('}'))
            throw new ParseException("switch 表达式需要 { 常量 => 表达式, _ => 表达式 }。", keyword);
        var selectorValue = parse(text[..keyword].TrimEnd(), null);
        ValidateSelector(selectorValue.Type, keyword);
        var selector = Expression.Variable(selectorValue.Type, "__crules_switch_selector");
        List<(ConstantExpression? Label, Expression Value)> arms = [];
        HashSet<object?> constants = [];
        int end = text.TrimEnd().Length - 1;
        int at = open + 1;
        while (at < end)
        {
            while (at < end && char.IsWhiteSpace(text[at])) at++;
            if (at == end) break;
            int comma = Find(text, at, ",");
            int armEnd = comma < 0 ? end : comma;
            string arm = text[at..armEnd];
            int arrow = Find(arm, 0, "=>");
            if (arrow < 0) throw new ParseException("switch 分支缺少 =>。", at);
            string label = arm[..arrow].Trim();
            bool fallback = label == "_";
            if (arms.Any(a => a.Label is null)) throw new ParseException("_ 默认分支必须位于最后。", at);
            ConstantExpression? literal = null;
            try
            {
                if (!fallback)
                {
                    literal = Label(label, selector.Type, parse, at, audit);
                    if (!constants.Add(literal.Value)) throw new ParseException("重复的 switch 常量。", at);
                }
                arms.Add((literal, parse(arm[(arrow + 2)..].Trim(), expected)));
            }
            catch (CompileFailure failure) { throw new ParseException(failure.Message, at); }
            catch (ParseException error) { throw new ParseException(error.Message, at); }
            at = armEnd + 1;
        }
        if (arms.Count == 0 || arms[^1].Label is not null) throw new ParseException("switch 表达式必须包含最后的 _ 默认分支。", keyword);
        Expression result = arms[^1].Value;
        for (int i = arms.Count - 2; i >= 0; i--)
        {
            var test = Expression.Equal(selector, arms[i].Label!);
            // 统一结果类型及转换交由上游的条件表达式绑定。
            var condition = Expression.Parameter(typeof(bool), "__crules_choose");
            var left = Expression.Parameter(arms[i].Value.Type, "__crules_left");
            var right = Expression.Parameter(result.Type, "__crules_right");
            var adapter = new DynamicExpresso.Interpreter();
            var converted = expected is null
                ? adapter.Parse("__crules_choose ? __crules_left : __crules_right", new DynamicExpresso.Parameter(condition), new DynamicExpresso.Parameter(left), new DynamicExpresso.Parameter(right)).Expression
                : adapter.Parse("__crules_choose ? __crules_left : __crules_right", expected, new DynamicExpresso.Parameter(condition), new DynamicExpresso.Parameter(left), new DynamicExpresso.Parameter(right)).Expression;
            result = new Substitute(new() { [condition] = test, [left] = arms[i].Value, [right] = result }).Visit(converted);
        }
        return Expression.Block([selector], Expression.Assign(selector, selectorValue), result);
    }

    private sealed class Substitute(Dictionary<ParameterExpression, Expression> values) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => values.GetValueOrDefault(node, node);
    }

    private static int Find(string text, int start, string token)
    {
        int depth = 0;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] is '"' or '\'' || text[i] == '@' && i + 1 < text.Length && text[i + 1] == '"')
            { i = StatementParser.SkipLiteral(text, i) - 1; continue; }
            if (depth == 0 && text.AsSpan(i).StartsWith(token, StringComparison.Ordinal)
                && (token != "switch" || (i == 0 || !char.IsLetterOrDigit(text[i - 1]) && text[i - 1] != '_')
                    && (i + 6 == text.Length || !char.IsLetterOrDigit(text[i + 6]) && text[i + 6] != '_'))) return i;
            if (text[i] is '(' or '[' or '{') depth++;
            else if (text[i] is ')' or ']' or '}') { if (depth == 0) return -1; depth--; }
        }
        return -1;
    }
}
