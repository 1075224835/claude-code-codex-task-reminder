using System.Text.Json.Serialization;
using Reminder.Protocol.Types;

namespace Reminder.Protocol.Dtos;

/// <summary>
/// 一条提醒的明文负载（加密前）。线字段名固定，安卓端须逐一对齐。
/// （类型名用 ReminderMessage 而非 Reminder，避免与根命名空间 Reminder 冲突。）
/// </summary>
public sealed record ReminderMessage
{
    [JsonPropertyName("v")]          public int Version { get; init; } = ProtocolConstants.Version;
    [JsonPropertyName("id")]         public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("type")]       public string Type { get; init; } = ReminderTypes.TaskComplete;
    [JsonPropertyName("host")]       public string Host { get; init; } = "";
    [JsonPropertyName("project")]    public string Project { get; init; } = "";
    [JsonPropertyName("cwd")]        public string Cwd { get; init; } = "";
    [JsonPropertyName("session_id")] public string SessionId { get; init; } = "";
    [JsonPropertyName("agent")]      public string Agent { get; init; } = "";
    [JsonPropertyName("title")]      public string Title { get; init; } = "";
    [JsonPropertyName("detail")]     public string Detail { get; init; } = "";
    [JsonPropertyName("created_at")] public string CreatedAt { get; init; } = "";
    /// <summary>内层随机 nonce（16B base64），与外层 GCM nonce 无关，仅用于内容去重熵。</summary>
    [JsonPropertyName("nonce")]      public string Nonce { get; init; } = "";

    /// <summary>渲染用：把模板里的占位符替换为本条提醒的字段。</summary>
    public string Render(string template) => template
        .Replace("{host}", Host)
        .Replace("{project}", Project)
        .Replace("{cwd}", Cwd)
        .Replace("{path}", Cwd)
        .Replace("{session}", SessionId)
        .Replace("{agent}", Agent)
        .Replace("{detail}", string.IsNullOrEmpty(Detail) ? "" : Detail)
        .Replace("{time}", CreatedAt);
}
