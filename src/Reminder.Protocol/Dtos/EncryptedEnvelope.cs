using System.Text.Json.Serialization;

namespace Reminder.Protocol.Dtos;

/// <summary>
/// 加密信封 —— 实际通过 HTTPS POST 发送的 body，也是 FCM data 消息携带的内容。
/// 头部字段 (kid/did/ts) 既用于路由与密钥定位，也作为 AES-GCM 的 AAD 被认证。
/// </summary>
public sealed record EncryptedEnvelope
{
    /// <summary>工作区主密钥 id（支持轮换）。</summary>
    [JsonPropertyName("kid")]   public string Kid { get; init; } = "";
    /// <summary>发送方设备 id（用于定位其 token → 派生消息密钥）。</summary>
    [JsonPropertyName("did")]   public string Did { get; init; } = "";
    /// <summary>Unix 秒时间戳（新鲜度/防重放）。</summary>
    [JsonPropertyName("ts")]    public long Ts { get; init; }
    /// <summary>AES-GCM nonce/IV（12B，base64）。</summary>
    [JsonPropertyName("nonce")] public string Nonce { get; init; } = "";
    /// <summary>密文（base64）。</summary>
    [JsonPropertyName("ct")]    public string Ct { get; init; } = "";
    /// <summary>GCM 认证标签（16B，base64）。</summary>
    [JsonPropertyName("tag")]   public string Tag { get; init; } = "";
}
