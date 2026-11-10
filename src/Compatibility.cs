using System.Runtime.CompilerServices;

namespace CRules;

// Internal helpers for APIs absent from .NET Standard 2.0. No public surface changes.
internal static class Compatibility
{
    internal static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    internal static bool IsByRefLike(Type type) => type.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute");
}

internal static class LegacyExtensions
{
    internal static bool StartsWith(this string text, char value) => text.Length != 0 && text[0] == value;
    internal static bool EndsWith(this string text, char value) => text.Length != 0 && text[text.Length - 1] == value;
    internal static bool Contains(this string text, char value) => text.IndexOf(value) >= 0;
    internal static bool Contains(this string text, string value, StringComparison comparison) => text.IndexOf(value, comparison) >= 0;
    internal static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> values, TKey key, TValue fallback) => values.TryGetValue(key, out var value) ? value : fallback;
    internal static string Replace(this string text, string oldValue, string newValue, StringComparison comparison)
    {
        if (comparison != StringComparison.Ordinal) throw new ArgumentOutOfRangeException(nameof(comparison));
        return text.Replace(oldValue, newValue);
    }
}

internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
    internal static ReferenceEqualityComparer Instance { get; } = new();
    public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
    public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
}
