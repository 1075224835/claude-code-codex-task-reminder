using System.Text.Json.Serialization;

namespace Reminder.Protocol.Dtos;

/// <summary>
/// 确认（ack）负载 —— 当某端关闭一条提醒时回传，经与 Reminder 相同的信封机制加密。
/// </summary>
public sealed record AckMessage
{
    [JsonPropertyName("id")]     public string Id { get; init; } = "";
    [JsonPropertyName("did")]    public string Did { get; init; } = "";
    [JsonPropertyName("ack_at")] public string AckAt { get; init; } = "";
}
