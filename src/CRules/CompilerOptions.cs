using System.Reflection;

namespace CRules;

/// <summary>M0 可信宿主原型配置。尚不提供进程内不可信脚本沙箱。</summary>
public sealed record CompilerOptions
{
    public bool DecimalOnly { get; init; }
    /// <summary>NativeAOT 必须显式选择 Interpreted；默认不会静默退回解释执行。</summary>
    public RuleExecutionMode ExecutionMode { get; init; } = RuleExecutionMode.JitCompiled;
    /// <summary>实验选项。标准 Compile 的嵌套 Lambda 可能在执行时调用 MethodInfo.CreateDelegate。</summary>
    public bool AllowRuntimeLambdaBinding { get; init; }
    public int MaxSourceLength { get; init; } = 65536;
    public int MaxExpressionLength { get; init; } = 16384;
    public int MaxStatements { get; init; } = 1000;
    public int MaxDepth { get; init; } = 32;
    public int MaxLocals { get; init; } = 256;
    public int MaxTreeNodes { get; init; } = 50000;
    /// <summary>补充规则可见的类型。输入、返回类型及其泛型实参/数组元素类型自动注册，无需重复添加。</summary>
    public IReadOnlyList<Type> ReferencedTypes { get; init; } = [];
    /// <summary>可选的精确成员策略。非空时检查表达式中所有成员及方法。</summary>
    public Func<MemberInfo, bool>? IsMemberAllowed { get; init; }
}
