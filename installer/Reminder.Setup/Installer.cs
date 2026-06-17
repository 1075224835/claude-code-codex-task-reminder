using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace Reminder.Setup;

/// <summary>安装/卸载核心逻辑：解压内嵌负载 → 写快捷方式与开机自启 → 启动。UI 与静默模式共用。</summary>
public static class Installer
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string RecvDir  => Path.Combine(Local, "FullscreenReminder");
    public static string AgentDir => Path.Combine(Local, "reminder-agent");

    public static void Install(bool receiver, bool sender, bool claudeHooks = false, Action<string>? log = null)
    {
        log ??= _ => { };

        KillRunning();
        ExtractPayload(receiver, sender);

        string startup   = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "全屏提醒");
        Directory.CreateDirectory(startMenu);

        if (receiver)
        {
            string exe = Path.Combine(RecvDir, "FullscreenReminder.exe");
            CreateShortcut(Path.Combine(startup, "FullscreenReminder.lnk"), exe, RecvDir, "全屏提醒 接收端");
            CreateShortcut(Path.Combine(startMenu, "全屏提醒 接收端.lnk"), exe, RecvDir, "全屏提醒 接收端");
            log("接收端已安装并设为开机自启。");
            Launch(exe);
        }
        if (sender)
        {
            string exe = Path.Combine(AgentDir, "reminder-agent.exe");
            CreateShortcut(Path.Combine(startup, "reminder-agent.lnk"), exe, AgentDir, "全屏提醒 发送端");
            CreateShortcut(Path.Combine(startMenu, "全屏提醒 发送端.lnk"), exe, AgentDir, "全屏提醒 发送端");
            TryDelete(Path.Combine(startup, "reminder-agent-watch.vbs")); // 清理旧的隐藏 VBS 自启
            log("发送端已安装并设为开机自启（托盘）。");

            if (claudeHooks)
            {
                try
                {
                    var p = Process.Start(new ProcessStartInfo(exe, "install-claude-hooks") { UseShellExecute = false, CreateNoWindow = true });
                    p?.WaitForExit(5000);
                    log("已接入 Claude Code（写入钩子，重启 Claude Code 生效）。");
                }
                catch { /* 忽略 */ }
            }
            Launch(exe);
        }
    }

    public static void Uninstall(bool purge, Action<string>? log = null)
    {
        log ??= _ => { };
        KillRunning();

        string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        foreach (var f in new[] { "FullscreenReminder.lnk", "reminder-agent.lnk", "reminder-agent-watch.vbs" })
            TryDelete(Path.Combine(startup, f));
        TryDeleteDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "全屏提醒"));
        TryDeleteDir(RecvDir);
        TryDeleteDir(AgentDir);

        if (purge)
        {
            TryDeleteDir(Path.Combine(AppData, "ReminderHub"));
            TryDeleteDir(Path.Combine(AppData, "reminder-agent"));
            log("已卸载，并删除全部配置/密钥。");
        }
        else log("已卸载（配置/密钥保留）。");
    }

    private static void ExtractPayload(bool receiver, bool sender)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("Reminder.Setup.payload.zip")
            ?? throw new InvalidOperationException("安装包损坏：缺少内嵌负载。");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var e in zip.Entries)
        {
            if (string.IsNullOrEmpty(e.Name)) continue; // 目录项
            string norm = e.FullName.Replace('\\', '/');
            string top = norm.Split('/')[0];
            string root;
            if (top == "FullscreenReminder")
            {
                if (!receiver) continue;
                root = RecvDir;
            }
            else if (top == "reminder-agent")
            {
                if (!sender) continue;
                root = AgentDir;
            }
            else
            {
                throw new InvalidOperationException($"安装包损坏：包含非法路径 {e.FullName}");
            }

            string rel = norm[(top.Length + 1)..];
            if (Path.IsPathRooted(rel) || rel.Split('/').Any(p => p == ".."))
                throw new InvalidOperationException($"安装包损坏：包含路径穿越 {e.FullName}");

            string rootFull = EnsureTrailingSeparator(Path.GetFullPath(root));
            string dest = Path.GetFullPath(Path.Combine(rootFull, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!dest.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"安装包损坏：包含越界路径 {e.FullName}");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, overwrite: true);
        }
    }

    private static void KillRunning()
    {
        foreach (var name in new[] { "FullscreenReminder", "reminder-agent" })
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { /* 忽略 */ }
            }
        System.Threading.Thread.Sleep(300);
    }

    private static void CreateShortcut(string lnkPath, string target, string workdir, string desc)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var sc = shell.CreateShortcut(lnkPath);
            sc.TargetPath = target;
            sc.WorkingDirectory = workdir;
            sc.Description = desc;
            sc.Save();
        }
        catch { /* 忽略快捷方式失败 */ }
    }

    private static void Launch(string exe)
    {
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) }); }
        catch { /* 忽略 */ }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
    private static void TryDeleteDir(string path) { try { Directory.Delete(path, true); } catch { } }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
