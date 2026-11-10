using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using DynamicExpresso;
using DynamicExpresso.Exceptions;

namespace CRules;

/// <summary>上游公开 API 的窄兼容适配；不在执行阶段解析或转换源码。</summary>
internal static class ExpressionCompatibility
{
    private static readonly Regex ScientificFloat = new(@"\G(?:\d+(?:\.\d*)?|\.\d+)[eE][+-]?\d+[fF]\b", RegexOptions.CultureInvariant);
    private static readonly Regex CoalesceNames = new(@"\b([A-Za-z_]\w*)\s*\?\?\s*([A-Za-z_]\w*)\b", RegexOptions.CultureInvariant);

    internal static Expression Parse(Interpreter interpreter, string text, Type? expected, Parameter[] parameters)
    {
        StringBuilder normalized = new();
        List<(int Source, int Length, int Target, int TargetLength)> replacements = [];
        for (int at = 0; at < text.Length;)
        {
            if (text[at] is '"' or '\'' || text[at] == '@' && at + 1 < text.Length && text[at + 1] == '"')
            { int end = StatementParser.SkipLiteral(text, at); normalized.Append(text.AsSpan(at, end - at)); at = end; continue; }
            if (at + 1 < text.Length && (text.AsSpan(at, 2).SequenceEqual("++") || text.AsSpan(at, 2).SequenceEqual("--")))
                throw new ParseException("Syntax 1.0 不支持 ++/--；请使用局部 +=/-=。", at);
            var match = at == 0 || !char.IsLetterOrDigit(text[at - 1]) && text[at - 1] != '_' ? ScientificFloat.Match(text, at) : Match.Empty;
            if (match.Success && match.Index == at)
            {
                if (!float.TryParse(match.ValueSpan[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                    throw new ParseException("float 字面量超出范围。", at);
                string alias = "__crules_float_literal_" + at;
                interpreter.SetVariable(alias, value);
                replacements.Add((at, match.Length, normalized.Length, alias.Length));
                normalized.Append(alias); at += match.Length;
            }
            else normalized.Append(text[at++]);
        }
        string expression = normalized.ToString();
        try
        {
            try { return Bind(interpreter, expression, expected, parameters); }
            catch (ParseException)
            {
                // DynamicExpresso 2.19.6 遗漏 char/char? 的部分隐式数值提升。
                // 只补充命名操作数之间的 ??，其余绑定继续由上游决定。
                foreach (Match match in CoalesceNames.Matches(expression))
                {
                    if (InsideLiteral(expression, match.Index)) continue;
                    Expression? left = parameters.FirstOrDefault(p => p.Name == match.Groups[1].Value)?.Expression;
                    Expression? right = parameters.FirstOrDefault(p => p.Name == match.Groups[2].Value)?.Expression;
                    if (left is null || right is null) continue;
                    var lt = Nullable.GetUnderlyingType(left.Type);
                    var rt = Nullable.GetUnderlyingType(right.Type) ?? right.Type;
                    if (lt is null) continue;
                    if (lt == typeof(char) && CharPromotesTo(rt)) left = Expression.Convert(left, typeof(Nullable<>).MakeGenericType(rt));
                    else if (rt == typeof(char) && CharPromotesTo(lt)) right = Expression.Convert(right, Nullable.GetUnderlyingType(right.Type) is null ? lt : typeof(Nullable<>).MakeGenericType(lt));
                    else continue;
                    var coalesce = Expression.Coalesce(left, right);
                    var slot = Expression.Parameter(coalesce.Type, "__crules_coalesce_value");
                    string rewritten = expression[..match.Index] + slot.Name + expression[(match.Index + match.Length)..];
                    var bound = Bind(interpreter, rewritten, expected, parameters.Append(new Parameter(slot)).ToArray());
                    return new Replace(slot, coalesce).Visit(bound);
                }
                throw;
            }
        }
        catch (ParseException error) when (replacements.Count > 0)
        {
            int offset = error.Position;
            foreach (var item in replacements)
            {
                if (error.Position < item.Target) break;
                offset = error.Position < item.Target + item.TargetLength ? item.Source : error.Position + item.Source + item.Length - item.Target - item.TargetLength;
            }
            throw new ParseException(error.Message, offset);
        }
    }
    private static Expression Bind(Interpreter interpreter, string text, Type? expected, Parameter[] parameters)
    {
        var value = interpreter.Parse(text, parameters).Expression;
        return expected is null ? value : ConvertImplicit(interpreter, value, expected);
    }

    internal static Expression ConvertImplicit(Interpreter interpreter, Expression value, Type target)
    {
        if (value.Type == target) return value;
        if (value.Type == typeof(object) && (value is DynamicExpression
            || value is InvocationExpression invocation && DynamicCalls.IsCallSite(invocation)))
            return Expression.Convert(value, target);
        // 上游 Parse(text, expected) 会插入显式转换，甚至允许 null -> 非可空值类型。
        // 声明、赋值和返回需要隐式转换，不能把错误推迟到 Execute。
        if (value is ConstantExpression { Value: null })
        {
            if (!target.IsValueType || Nullable.GetUnderlyingType(target) is not null) return Expression.Constant(null, target);
            throw new ParseException($"null 不能赋给非可空类型 {target.Name}。", 0);
        }
        if (Numeric(value.Type) && (Numeric(target) || (Nullable.GetUnderlyingType(target) ?? target).IsEnum))
        {
            if (!NumericConversions.Implicit(value.Type, target, (value as ConstantExpression)?.Value, value is ConstantExpression))
                throw new ParseException($"不能从 {value.Type.Name} 隐式转换为 {target.Name}。", 0);
            return Expression.Convert(value, target);
        }
        var source = Expression.Parameter(value.Type, "__crules_conversion_source");
        var fallback = Expression.Parameter(target, "__crules_conversion_target");
        var choose = Expression.Parameter(typeof(bool), "__crules_conversion_choose");
        var binding = interpreter.Parse("__crules_conversion_choose ? __crules_conversion_source : __crules_conversion_target",
            new Parameter(choose), new Parameter(source), new Parameter(fallback)).Expression;
        if (binding.Type != target || binding is not ConditionalExpression conditional)
            throw new ParseException($"不能从 {value.Type.Name} 隐式转换为 {target.Name}。", 0);
        return new Replace(source, value).Visit(conditional.IfTrue);
    }

    private static bool Numeric(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return !type.IsEnum && (type == typeof(char) || Type.GetTypeCode(type) is >= TypeCode.SByte and <= TypeCode.Decimal);
    }
    internal static bool CharPromotesTo(Type type) => type == typeof(ushort) || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    private static bool InsideLiteral(string text, int at)
    {
        for (int i = 0; i < at; i++)
            if (text[i] is '"' or '\'' || text[i] == '@' && i + 1 < text.Length && text[i + 1] == '"')
            { i = StatementParser.SkipLiteral(text, i) - 1; if (i >= at) return true; }
        return false;
    }
    private sealed class Replace(ParameterExpression slot, Expression value) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == slot ? value : node;
    }
}
