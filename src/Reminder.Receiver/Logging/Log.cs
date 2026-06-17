using System.IO;

namespace Reminder.Receiver.Logging;

/// <summary>极简文件日志（离线环境不引入 Serilog）。线程安全、失败静默。</summary>
public static class Log
{
    private static readonly object _lock = new();
    private static string _file = "";

    public static void Init(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            _file = Path.Combine(dir, $"receiver-{DateTime.Now:yyyyMMdd}.log");
        }
        catch { /* 忽略 */ }
    }

    public static void Info(string m) => Write("INFO", m);
    public static void Warn(string m) => Write("WARN", m);
    public static void Error(string m) => Write("ERROR", m);

    private static void Write(string level, string m)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {m}";
        try
        {
            lock (_lock)
            {
                if (_file.Length > 0) File.AppendAllText(_file, line + Environment.NewLine);
            }
        }
        catch { /* 忽略 */ }
        System.Diagnostics.Debug.WriteLine(line);
    }
}
