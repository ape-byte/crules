namespace CRules;

internal static class VisibleTypes
{
    internal static IReadOnlyList<Type> Collect(IEnumerable<Type> types)
    {
        HashSet<Type> seen = [];
        void Add(Type type)
        {
            if (!seen.Add(type)) return;
            if (type.HasElementType) Add(type.GetElementType()!);
            if (type.IsConstructedGenericType)
            {
                Add(type.GetGenericTypeDefinition());
                foreach (var argument in type.GetGenericArguments()) Add(argument);
            }
        }
        foreach (var type in types) Add(type);
        var definitions = seen.Where(t => !t.HasElementType && !t.IsConstructedGenericType).ToArray();
        var conflict = definitions.GroupBy(t => t.Name.Split('`')[0], StringComparer.Ordinal)
            .FirstOrDefault(g => g.Select(t => t.FullName?.Split('`')[0]).Distinct().Count() > 1);
        if (conflict is not null)
            throw new CompileFailure("CRL2002", $"可见类型名称冲突：{conflict.Key}。", new(0, 0));
        return definitions;
    }
}
