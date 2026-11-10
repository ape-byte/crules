using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Dynamic;

namespace CRules;

internal sealed class DynamicCalls : ExpressionVisitor
{
    internal static bool IsCallSite(InvocationExpression node)
        => node.Expression is MemberExpression { Member.Name: "Target", Expression: ConstantExpression { Value: CallSite site } }
        && (site.Binder?.GetType().Assembly == typeof(DynamicExpresso.Interpreter).Assembly
            || site.Binder?.GetType().Assembly == typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly);
    protected override Expression VisitInvocation(InvocationExpression node)
    {
        var updated = base.VisitInvocation(node);
        if (IsCallSite(node) && node.Type == typeof(object))
            return Expression.Call(typeof(DynamicCalls), nameof(CheckValue), null, updated);
        return updated;
    }
    public static object? CheckValue(object? value)
    {
        if (value is float or double) throw new InvalidOperationException("DecimalOnly 动态值不接受 float/double。");
        return value;
    }
}
