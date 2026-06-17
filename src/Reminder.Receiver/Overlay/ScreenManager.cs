using Reminder.Receiver.Prefs;

namespace Reminder.Receiver.Overlay;

/// <summary>一台物理显示器（物理像素坐标）。</summary>
public readonly record struct MonitorInfo(string DeviceName, int Left, int Top, int Width, int Height, bool Primary);

/// <summary>显示器枚举与「该提醒应铺到哪些屏」的解析。</summary>
public static class ScreenManager
{
    public static List<MonitorInfo> All() => NativeMethods.GetMonitors();

    public static MonitorInfo Primary()
    {
        var all = All();
        return all.FirstOrDefault(m => m.Primary, all.Count > 0 ? all[0] : default);
    }

    /// <summary>
    /// 解析目标屏：Enabled 为空=全部屏；否则取设备名匹配项；若都不匹配（换线/休眠后漂移），
    /// 按 ShowOnAllIfUnmatched 回退到全部或主屏。
    /// </summary>
    public static List<MonitorInfo> ResolveTargets(MonitorPrefs prefs)
    {
        var all = All();
        if (all.Count == 0) return all;
        if (prefs.Enabled.Count == 0) return all;

        var matched = all.Where(m => prefs.Enabled.Contains(m.DeviceName)).ToList();
        if (matched.Count > 0) return matched;
        return prefs.ShowOnAllIfUnmatched ? all : new List<MonitorInfo> { Primary() };
    }
}
