using System.Text;
using System.Text.Json;

namespace CRules;

/// <summary>动态字段的存在状态；Value 不代表字段符合某个读取类型。</summary>
public enum MetadataValueState { Missing, Null, Value }

/// <summary>Metadata 缺少必需值、类型不符或数值超范围。消息不包含原始字段值。</summary>
public sealed class MetadataReadException(string key, string expectedType, MetadataValueState state)
    : InvalidOperationException($"Metadata 字段 '{key}' 无法读取为 {expectedType}（状态：{state}）。")
{
    public string Key { get; } = key;
    public string ExpectedType { get; } = expectedType;
    public MetadataValueState State { get; } = state;
}

/// <summary>不可变、有类型读取的 JSON 对象快照。键区分大小写，不进行隐式字符串/数值转换。</summary>
public sealed class RuleMetadata
{
    private readonly JsonElement _root;
    private RuleMetadata(JsonElement root) => _root = root;
    public static RuleMetadata Empty { get; } = FromJson("{}");

    /// <summary>在上下文构建时解析一次；限制 UTF-8 字节数和 JSON 深度，拒绝重复键。</summary>
    public static RuleMetadata FromJson(string json, int maxBytes = 262144, int maxDepth = 16)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (maxDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));
        if (Encoding.UTF8.GetByteCount(json) > maxBytes) throw new ArgumentException("Metadata 超过字节上限。", nameof(json));
        using var document = JsonDocument.Parse(json, new() { MaxDepth = maxDepth });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Metadata 必须是 JSON 对象。", nameof(json));
        ValidateKeys(document.RootElement);
        return new(document.RootElement.Clone());
    }

    /// <summary>复制外部 JsonElement，结果不依赖调用方 JsonDocument 的生命周期。</summary>
    public static RuleMetadata FromJson(JsonElement value, int maxBytes = 262144, int maxDepth = 16)
        => FromJson(value.GetRawText(), maxBytes, maxDepth);

    /// <summary>构建时序列化为独立快照；字典值须为宿主认可的 JSON 数据。</summary>
    public static RuleMetadata FromDictionary(IReadOnlyDictionary<string, object?> values,
        int maxBytes = 262144, int maxDepth = 16)
    {
        ArgumentNullException.ThrowIfNull(values);
        return FromJson(JsonSerializer.Serialize(values), maxBytes, maxDepth);
    }

    public MetadataValueState GetState(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return !_root.TryGetProperty(key, out var value) ? MetadataValueState.Missing
            : value.ValueKind == JsonValueKind.Null ? MetadataValueState.Null : MetadataValueState.Value;
    }
    public bool Contains(string key) => GetState(key) != MetadataValueState.Missing;
    public bool IsNull(string key) => GetState(key) == MetadataValueState.Null;
    public string? GetString(string key) => Read(key, "string", v => v.ValueKind == JsonValueKind.String, v => v.GetString()!);
    public bool? GetBoolean(string key) => ReadValue(key, "bool", v => v.ValueKind is JsonValueKind.True or JsonValueKind.False, v => v.GetBoolean());
    public long? GetInt64(string key) => ReadValue(key, "long", v => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out _), v => v.GetInt64());
    public decimal? GetDecimal(string key) => ReadValue(key, "decimal", v => v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out _), v => v.GetDecimal());
    public RuleMetadata? GetObject(string key) => Read(key, "object", v => v.ValueKind == JsonValueKind.Object, v => new RuleMetadata(v));
    public string RequireString(string key) => GetString(key) ?? throw Error(key, "string");
    public bool RequireBoolean(string key) => GetBoolean(key) ?? throw Error(key, "bool");
    public long RequireInt64(string key) => GetInt64(key) ?? throw Error(key, "long");
    public decimal RequireDecimal(string key) => GetDecimal(key) ?? throw Error(key, "decimal");
    public RuleMetadata RequireObject(string key) => GetObject(key) ?? throw Error(key, "object");

    private T? Read<T>(string key, string type, Func<JsonElement, bool> valid, Func<JsonElement, T> get) where T : class
    {
        if (GetState(key) != MetadataValueState.Value) return null;
        var value = _root.GetProperty(key);
        return valid(value) ? get(value) : throw Error(key, type);
    }
    private T? ReadValue<T>(string key, string type, Func<JsonElement, bool> valid, Func<JsonElement, T> get) where T : struct
    {
        if (GetState(key) != MetadataValueState.Value) return null;
        var value = _root.GetProperty(key);
        return valid(value) ? get(value) : throw Error(key, type);
    }
    private MetadataReadException Error(string key, string type) => new(key, type, GetState(key));
    private static void ValidateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> keys = new(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new ArgumentException("Metadata 包含重复键。");
                ValidateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ValidateKeys(item);
    }
}
