using System.Text.Json.Serialization;

namespace Reminder.Protocol.Dtos;

/// <summary>统计快照 —— GET /v1/stats 的响应（明文 JSON，经传输层 TLS 保护）。</summary>
public sealed record StatsSnapshot
{
    [JsonPropertyName("generated_at")] public string GeneratedAt { get; init; } = "";
    /// <summary>各消息类型的总计数。key = 类型 key。</summary>
    [JsonPropertyName("totals")]       public Dictionary<string, long> Totals { get; init; } = new();
    /// <summary>按设备+类型细分。</summary>
    [JsonPropertyName("by_device")]    public List<DeviceTypeCount> ByDevice { get; init; } = new();
}

public sealed record DeviceTypeCount
{
    [JsonPropertyName("did")]   public string Did { get; init; } = "";
    [JsonPropertyName("host")]  public string Host { get; init; } = "";
    [JsonPropertyName("type")]  public string Type { get; init; } = "";
    [JsonPropertyName("count")] public long Count { get; init; }
}
