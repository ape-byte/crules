using System.Linq.Expressions;
using DynamicExpresso.Exceptions;

namespace CRules;

internal static class DictionarySupport
{
    internal static bool ReturnType(Type type) => type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
        || type.GetGenericTypeDefinition() == typeof(IDictionary<,>) || type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
    internal static Type? Contract(Type type) => type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IDictionary<,>) || type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
        ? type : type.GetInterfaces().FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IDictionary<,>));

    internal static Expression? Initialize(NewExpression creation, string body, Func<string, Type?, Expression> parse)
    {
        if (!creation.Type.IsGenericType || creation.Type.GetGenericTypeDefinition() != typeof(Dictionary<,>)) return null;
        var types = creation.Type.GetGenericArguments();
        var dictionary = Expression.Variable(creation.Type, "__crules_dictionary");
        List<Expression> expressions = [Expression.Assign(dictionary, creation)];
        bool? indexed = null;
        foreach (var item in ArraySupport.Items(body))
        {
            bool index = item.StartsWith('[');
            if (indexed is bool previous && previous != index) throw new ParseException("字典不能混合 Add 与索引初始化器。", 0);
            indexed = index;
            string key, value;
            if (index)
            {
                int end = DynamicExpressoAdapter.FindBoundary(item, 1, ']');
                if (end < 0 || !item[(end + 1)..].TrimStart().StartsWith('=')) throw new ParseException("字典索引初始化需要 [键] = 值。", 0);
                key = item[1..end]; value = item[(end + 1)..].TrimStart()[1..].Trim();
            }
            else
            {
                if (!item.StartsWith('{') || !item.EndsWith('}')) throw new ParseException("字典元素需要 { 键, 值 }。", 0);
                var pair = ArraySupport.Items(item[1..^1]);
                if (pair.Count != 2) throw new ParseException("字典元素必须包含一个键和一个值。", 0);
                key = pair[0]; value = pair[1];
            }
            var keyTree = parse(key, types[0]);
            var valueTree = parse(value, types[1]);
            expressions.Add(index
                ? Expression.Assign(Expression.MakeIndex(dictionary, creation.Type.GetProperty("Item")!, [keyTree]), valueTree)
                : Expression.Call(dictionary, creation.Type.GetMethod("Add", types)!, keyTree, valueTree));
        }
        expressions.Add(dictionary);
        return Expression.Block([dictionary], expressions);
    }
}
