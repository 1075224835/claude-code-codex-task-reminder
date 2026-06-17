using System.IO;

namespace Reminder.Receiver.Config;

/// <summary>接收端所有配置/数据文件位置：%APPDATA%\ReminderHub\。</summary>
public static class AppPaths
{
    public static string Base { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReminderHub");

    public static string Config   => Path.Combine(Base, "config.json");
    public static string Devices  => Path.Combine(Base, "devices.json");
    public static string Prefs    => Path.Combine(Base, "prefs.receiver.json");
    public static string Stats    => Path.Combine(Base, "stats.json");
    public static string Monitors => Path.Combine(Base, "monitors.json");
    public static string LogDir   => Path.Combine(Base, "logs");

    public static void EnsureBase() => Directory.CreateDirectory(Base);
}
