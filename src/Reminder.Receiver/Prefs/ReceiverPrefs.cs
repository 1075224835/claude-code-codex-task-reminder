using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Reminder.Protocol.Types;
using Reminder.Receiver.Config;

namespace Reminder.Receiver.Prefs;

/// <summary>单个消息类型的展示偏好（可编辑文字/开关/提示音/强调色）。</summary>
public sealed class TypePref
{
    [JsonPropertyName("key")]         public string Key { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("template")]    public string Template { get; set; } = "";
    [JsonPropertyName("enabled")]     public bool Enabled { get; set; } = true;   // 是否弹全屏（false=忽略，仅计数）
    [JsonPropertyName("color")]       public string Color { get; set; } = "#37474F";
    [JsonPropertyName("sound")]       public string Sound { get; set; } = "";      // 空=系统默认；否则为 wav 文件路径
    /// <summary>该类型的自动关闭秒数；null=继承全局；0=不自动关闭（常驻直到手动关闭）。</summary>
    [JsonPropertyName("auto_close_seconds")] public int? AutoCloseSeconds { get; set; }
}

/// <summary>接收端展示偏好：全屏背景 + 各类型设置。prefs.receiver.json。</summary>
public sealed class ReceiverPrefs
{
    /// <summary>全屏背景：#RRGGBB 纯色，或图片文件路径（自动按 cover 适配，叠加暗色蒙版保证文字可读）。</summary>
    [JsonPropertyName("background")] public string Background { get; set; } = "#0E1116";
    /// <summary>全局自动关闭秒数（0=不自动关闭）。各类型可单独覆盖。默认 10 秒。</summary>
    [JsonPropertyName("auto_close_seconds")] public int AutoCloseSeconds { get; set; } = 10;
    [JsonPropertyName("types")]      public List<TypePref> Types { get; set; } = new();

    public static ReceiverPrefs CreateDefault()
    {
        var p = new ReceiverPrefs();
        foreach (var key in ReminderTypes.Defaults)
            p.Types.Add(new TypePref
            {
                Key = key,
                DisplayName = ReminderTypes.DefaultDisplayName(key),
                Template = ReminderTypes.DefaultTemplate(key),
                Color = ReminderTypes.DefaultColor(key),
                Enabled = true,
                // 需要人工干预的两类默认不自动关闭（留到手动处理）；其余继承全局倒计时。
                AutoCloseSeconds = (key is ReminderTypes.NeedsInput or ReminderTypes.NeedsApproval) ? 0 : null,
            });
        return p;
    }

    /// <summary>解析某类型的自动关闭秒数：类型覆盖优先，否则用全局。</summary>
    public int ResolveAutoClose(string key) => ForType(key).AutoCloseSeconds ?? AutoCloseSeconds;

    /// <summary>取某类型偏好；未配置则回退到默认值。</summary>
    public TypePref ForType(string key)
        => Types.FirstOrDefault(t => t.Key == key) ?? new TypePref
        {
            Key = key,
            DisplayName = ReminderTypes.DefaultDisplayName(key),
            Template = ReminderTypes.DefaultTemplate(key),
            Color = ReminderTypes.DefaultColor(key),
            Enabled = true,
        };
}

public sealed class PrefsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    public ReceiverPrefs Prefs { get; private set; } = new();

    public void Load()
    {
        if (File.Exists(AppPaths.Prefs))
            Prefs = JsonSerializer.Deserialize<ReceiverPrefs>(File.ReadAllText(AppPaths.Prefs), JsonOpts) ?? ReceiverPrefs.CreateDefault();
        else
        {
            Prefs = ReceiverPrefs.CreateDefault();
            Save();
        }
    }

    public void Save() => File.WriteAllText(AppPaths.Prefs, JsonSerializer.Serialize(Prefs, JsonOpts));
}
