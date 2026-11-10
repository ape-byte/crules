using System.Linq.Expressions;
using DynamicExpresso.Exceptions;

namespace CRules;

internal static class ArraySupport
{
    internal static Expression? Parse(string text, Func<string, Type?, Expression> parse, Func<string, Type?> resolve,
        Func<Expression, Type, Expression> bind)
    {
        if (!text.StartsWith("new", StringComparison.Ordinal) || text.Length < 4 || !char.IsWhiteSpace(text[3]) && text[3] != '[') return null;
        int bracket = Boundary(text, 3, '[');
        if (bracket < 0) return null;
        string typeName = text[3..bracket].Trim();
        int close = Boundary(text, bracket + 1, ']');
        if (close < 0) throw new ParseException("数组缺少 ]。", bracket);
        string bound = text[(bracket + 1)..close].Trim();
        if (Items(bound).Count > 1) throw new ParseException("Syntax 1.0 数组创建仅支持一维及一维交错数组。", bracket);
        int at = close + 1;
        string suffix = "";
        while (at < text.Length)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
            if (at >= text.Length || text[at] != '[') break;
            int end = Boundary(text, at + 1, ']');
            if (end < 0 || !string.IsNullOrWhiteSpace(text[(at + 1)..end])) throw new ParseException("交错数组的后续维度必须为 []。", at);
            suffix += "[]"; at = end + 1;
        }
        bool initializer = at < text.Length && text[at] == '{' && text.TrimEnd().EndsWith('}');
        if (!initializer && !string.IsNullOrWhiteSpace(text[at..])) return null;
        var items = initializer ? Items(text[(at + 1)..(text.TrimEnd().Length - 1)]) : [];
        Type element;
        if (typeName.Length == 0)
        {
            if (!initializer || bound.Length != 0 || suffix.Length != 0 || items.Count == 0) throw new ParseException("隐式数组需要非空初始化列表。", bracket);
            var values = items.Select(value => parse(value, null)).ToArray();
            var candidates = values.Where(value => value is not ConstantExpression { Value: null }).Select(value => value.Type).Distinct();
            element = candidates.FirstOrDefault(candidate => values.All(value => CanConvert(value, candidate, bind)))
                ?? throw new ParseException("数组元素没有共同类型。", bracket);
        }
        else element = resolve(typeName + suffix) ?? throw new ParseException("未知数组元素类型。", 3);
        if (initializer)
        {
            if (bound.Length != 0 && parse(bound, typeof(int)) is not ConstantExpression { Value: int size })
                throw new ParseException("含初始化列表的数组长度必须为常量。", bracket);
            if (bound.Length != 0 && ((ConstantExpression)parse(bound, typeof(int))).Value is int count && count != items.Count)
                throw new ParseException("数组长度与元素数量不一致。", bracket);
            return Expression.NewArrayInit(element, items.Select(value => ConvertElement(value, element, parse)));
        }
        if (bound.Length == 0) throw new ParseException("数组需要长度或初始化列表。", bracket);
        return Expression.NewArrayBounds(element, parse(bound, typeof(int)));
    }
    private static bool CanConvert(Expression value, Type target, Func<Expression, Type, Expression> bind)
    {
        try
        {
            if (value is ConstantExpression { Value: null }) return !target.IsValueType || Nullable.GetUnderlyingType(target) is not null;
            if (value.Type == typeof(char) && ExpressionCompatibility.CharPromotesTo(target)) return true;
            return bind(Expression.Parameter(value.Type), target).Type == target;
        }
        catch (ParseException) { return false; }
    }
    private static Expression ConvertElement(string text, Type target, Func<string, Type?, Expression> parse)
    {
        try { return parse(text, target); }
        catch (ParseException)
        {
            var value = parse(text, null);
            if (value.Type == typeof(char) && ExpressionCompatibility.CharPromotesTo(target)) return Expression.Convert(value, target);
            throw;
        }
    }
    internal static List<string> Items(string text)
    {
        List<string> values = [];
        for (int at = 0; at < text.Length;)
        {
            int comma = Boundary(text, at, ',');
            int end = comma < 0 ? text.Length : comma;
            string value = text[at..end].Trim();
            if (value.Length == 0) { if (string.IsNullOrWhiteSpace(text[at..])) break; throw new ParseException("缺少集合元素。", at); }
            values.Add(value); at = end + 1;
        }
        return values;
    }
    private static int Boundary(string text, int start, char delimiter)
    {
        int depth = 0;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] is '"' or '\'' || text[i] == '@' && i + 1 < text.Length && text[i + 1] == '"')
            { i = StatementParser.SkipLiteral(text, i) - 1; continue; }
            if (depth == 0 && text[i] == delimiter) return i;
            if (text[i] is '(' or '[' or '{') depth++;
            else if (text[i] is ')' or ']' or '}') { if (depth == 0) return -1; depth--; }
        }
        return -1;
    }
}
