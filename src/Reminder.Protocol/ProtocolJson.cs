using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reminder.Protocol;

/// <summary>
/// 全协议统一的 JSON 序列化配置。字段名由各 DTO 上的 [JsonPropertyName] 显式固定，
/// 以保证与安卓（Kotlin）端逐字节对齐。
/// </summary>
public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
        // 反序列化时容忍未知字段，便于协议向前兼容。
        PropertyNameCaseInsensitive = false,
    };

    public static byte[] ToUtf8<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T FromUtf8<T>(byte[] utf8) => JsonSerializer.Deserialize<T>(utf8, Options)!;

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
}
