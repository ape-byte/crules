using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;

namespace CRules;

internal sealed class ExpressionAudit(CompilerOptions options, SourceSpan span, bool allowRootLambda = false) : DynamicExpressionVisitor
{
    private int _nodes;
    private int _lambdaDepth;
    protected override Expression VisitLambda<T>(Expression<T> node)
    {
        if ((!allowRootLambda || _lambdaDepth > 0) && !options.AllowRuntimeLambdaBinding)
            throw new CompileFailure("CRL4003", "严格模式暂不接受嵌套 Lambda：标准 Compile 存在运行时委托绑定；仅可显式启用实验选项。", span, PositionPrecision.Expression);
        _lambdaDepth++;
        var result = base.VisitLambda(node);
        _lambdaDepth--;
        return result;
    }
    public override Expression? Visit(Expression? node)
    {
        if (node is null) return null;
        if (++_nodes > options.MaxTreeNodes) Reject("表达式树节点超过限额。");
        if (node is InvocationExpression invocation)
        {
            if (!DynamicCalls.IsCallSite(invocation) || options.IsMemberAllowed is not null)
                Reject("不允许任意委托调用，或动态访问无法通过静态成员策略验证。");
            if (options.DecimalOnly && invocation.Expression is MemberExpression { Expression: ConstantExpression { Value: System.Runtime.CompilerServices.CallSite { Binder: BinaryOperationBinder binary } } }
                && binary.Operation is ExpressionType.Add or ExpressionType.Subtract or ExpressionType.Multiply or ExpressionType.Divide or ExpressionType.Modulo)
                Reject("DecimalOnly 动态算术需要先显式转换为 decimal。");
        }
        if (node.NodeType == ExpressionType.Extension) Reject("未识别的上游扩展节点。");
        if (node.NodeType == ExpressionType.Dynamic && options.IsMemberAllowed is not null)
            Reject("动态成员无法通过静态成员策略验证，请使用有类型输入。");
        if (options.DecimalOnly && node.Type is { } type && (type == typeof(double) || type == typeof(float)
            || type == typeof(double?) || type == typeof(float?)))
            Reject("会计策略禁止 float/double，请使用 decimal 和 m 后缀。");
        return base.Visit(node);
    }
    protected override Expression VisitMember(MemberExpression node)
    { Check(node.Member); return base.VisitMember(node); }
    protected override Expression VisitMethodCall(MethodCallExpression node)
    { Check(node.Method); return base.VisitMethodCall(node); }
    protected override Expression VisitNew(NewExpression node)
    { if (node.Constructor is not null) Check(node.Constructor); return base.VisitNew(node); }
    protected override Expression VisitIndex(IndexExpression node)
    { if (node.Indexer is not null) Check(node.Indexer); return base.VisitIndex(node); }
    protected override MemberBinding VisitMemberBinding(MemberBinding node)
    {
        Check(node.Member);
        if (node.BindingType == MemberBindingType.Assignment && node.Member is PropertyInfo property && property.SetMethod is { } setter)
            Check(setter);
        return base.VisitMemberBinding(node);
    }
    protected override ElementInit VisitElementInit(ElementInit node)
    { Check(node.AddMethod); return base.VisitElementInit(node); }
    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType == ExpressionType.Assign && node.Left is IndexExpression { Indexer.SetMethod: { } setter }) Check(setter);
        if (options.DecimalOnly && node.NodeType is (ExpressionType.Add or ExpressionType.Subtract
            or ExpressionType.Multiply or ExpressionType.Divide or ExpressionType.Modulo
            or ExpressionType.AddChecked or ExpressionType.SubtractChecked or ExpressionType.MultiplyChecked)
            && node.Type != typeof(decimal) && node.Type != typeof(decimal?))
            Reject("会计算术须先转为 decimal，避免整数截断或溢出。");
        if (node.Method is not null) Check(node.Method);
        return base.VisitBinary(node);
    }
    protected override Expression VisitUnary(UnaryExpression node)
    { if (node.Method is not null) Check(node.Method); return base.VisitUnary(node); }

    private void Check(MemberInfo member)
    {
        var type = member.DeclaringType;
        var ns = type?.Namespace ?? "";
        if (type == typeof(Type) || type == typeof(Environment) || type == typeof(Random)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || ns.StartsWith("System.Reflection", StringComparison.Ordinal)
            || ns.StartsWith("System.IO", StringComparison.Ordinal)
            || ns.StartsWith("System.Net", StringComparison.Ordinal)
            || ns.StartsWith("System.Threading", StringComparison.Ordinal)
            || ns.StartsWith("System.Diagnostics", StringComparison.Ordinal)
            || ns.StartsWith("DynamicExpresso", StringComparison.Ordinal)
            || member.Name == "GetType") Reject($"不允许成员 {type?.Name}.{member.Name}。");
        if (options.IsMemberAllowed is not null && !options.IsMemberAllowed(member))
            Reject($"成员不在宿主白名单中：{type?.Name}.{member.Name}。");
    }
    private void Reject(string message) => throw new CompileFailure("CRL4002", message, span, PositionPrecision.Expression);
}
