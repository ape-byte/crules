using System.Runtime.CompilerServices;

namespace CRules;

internal static class RuntimeCompatibility
{
#if NETSTANDARD2_0
    internal static bool IsDynamicCodeSupported { get; } =
        typeof(object).Assembly.GetType("System.Runtime.CompilerServices.RuntimeFeature")
            ?.GetProperty("IsDynamicCodeSupported")?.GetValue(null) as bool? ?? true;
    internal static bool IsByRefLike(Type type) => Compatibility.IsByRefLike(type);
    internal static int Clamp(int value, int min, int max) => Compatibility.Clamp(value, min, max);
#else
    internal static bool IsDynamicCodeSupported => RuntimeFeature.IsDynamicCodeSupported;
    internal static bool IsByRefLike(Type type) => type.IsByRefLike;
    internal static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
#endif
}
