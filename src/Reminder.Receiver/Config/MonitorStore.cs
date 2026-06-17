using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reminder.Receiver.Prefs;

/// <summary>哪些屏显示提醒（monitors.json）。Enabled 为空=全部屏。</summary>
public sealed class MonitorPrefs
{
    /// <summary>启用的显示器设备名（如 \\.\DISPLAY1）。空=全部屏。</summary>
    [JsonPropertyName("enabled")] public List<string> Enabled { get; set; } = new();
    /// <summary>保存的屏都不匹配当前接线时，是否回退到全部屏（否则回退主屏）。</summary>
    [JsonPropertyName("show_on_all_if_unmatched")] public bool ShowOnAllIfUnmatched { get; set; } = true;
}

public sealed class MonitorStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    public MonitorPrefs Prefs { get; private set; } = new();

    public void Load()
    {
        if (File.Exists(Config.AppPaths.Monitors))
            Prefs = JsonSerializer.Deserialize<MonitorPrefs>(File.ReadAllText(Config.AppPaths.Monitors), JsonOpts) ?? new();
        else
        {
            Prefs = new MonitorPrefs();
            Save();
        }
    }

    public void Save() => File.WriteAllText(Config.AppPaths.Monitors, JsonSerializer.Serialize(Prefs, JsonOpts));
}
