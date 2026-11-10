namespace CRules;

internal static class NumericConversions
{
    internal static bool Implicit(Type source, Type target, object? literal = null, bool constant = false)
    {
        if (source == target) return true;
        if (constant && literal is null) return !target.IsValueType || Nullable.GetUnderlyingType(target) is not null;
        if (Nullable.GetUnderlyingType(source) is Type sourceValue)
        {
            if (Nullable.GetUnderlyingType(target) is not Type targetValue) return false;
            return Implicit(sourceValue, targetValue, literal, constant);
        }
        target = Nullable.GetUnderlyingType(target) ?? target;
        if (source == target) return true;
        if (target.IsEnum) return constant && source.IsPrimitive && source != typeof(bool) && source != typeof(char)
            && source != typeof(float) && source != typeof(double) && Convert.ToDecimal(literal) == 0;
        if (constant && source == typeof(int) && literal is int integer)
        {
            if (target == typeof(sbyte)) return integer is >= sbyte.MinValue and <= sbyte.MaxValue;
            if (target == typeof(byte)) return integer is >= byte.MinValue and <= byte.MaxValue;
            if (target == typeof(short)) return integer is >= short.MinValue and <= short.MaxValue;
            if (target == typeof(ushort)) return integer is >= ushort.MinValue and <= ushort.MaxValue;
            if (target == typeof(uint) || target == typeof(ulong)) return integer >= 0;
        }
        if (constant && source == typeof(long) && target == typeof(ulong) && literal is long longValue && longValue >= 0) return true;
        var targets = Type.GetTypeCode(source) switch
        {
            TypeCode.SByte => new[] { typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal) },
            TypeCode.Byte => [typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)],
            TypeCode.Int16 => [typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal)],
            TypeCode.UInt16 or TypeCode.Char => [typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)],
            TypeCode.Int32 => [typeof(long), typeof(float), typeof(double), typeof(decimal)],
            TypeCode.UInt32 => [typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)],
            TypeCode.Int64 or TypeCode.UInt64 => [typeof(float), typeof(double), typeof(decimal)],
            TypeCode.Single => [typeof(double)],
            _ => Type.EmptyTypes
        };
        return source == typeof(char) && target == typeof(ushort) || !source.IsEnum && targets.Contains(target);
    }
}
