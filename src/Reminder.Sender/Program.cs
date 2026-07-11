using System.IO;
using Reminder.Protocol.Dtos;
using Reminder.Protocol.Types;
using Reminder.Sender.Config;
using Reminder.Sender.HookParsers;
using Reminder.Sender.Sending;
using Reminder.Sender.Watchers;

namespace Reminder.Sender;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "tray";
        try
        {
            return cmd switch
            {
                "tray" or "run" => RunTray(),
                "enroll" => RunEnroll(args),
                "test"   => RunTest(),
                "send"   => RunSend(args),
                "hook"   => RunHook(args),
                "install-claude-hooks" => RunInstallClaudeHooks(args),
                "watch"  => RunWatchCli(args),
                _ => RunTray(),
            };
        }
        catch (Exception e)
        {
            LogSilent("致命错误: " + e);
            return 1;
        }
    }

    private static int RunTray()
    {
        new TrayApp().Run();
        return 0;
    }

    private static int RunEnroll(string[] a)
    {
        AgentCore.Enroll(GetOpt(a, "--code"));
        return 0;
    }

    private static int RunInstallClaudeHooks(string[] a)
    {
        string exe = Environment.ProcessPath ?? "reminder-agent.exe";
        var (ok, _, msg) = ClaudeHookInstaller.Install(exe, GetOpt(a, "--settings"));
        LogSilent("install-claude-hooks: " + msg);
        return ok ? 0 : 1;
    }

    private static int RunTest()
    {
        var (ok, status) = AgentCore.SendTestAsync().GetAwaiter().GetResult();
        LogSilent($"test 结果: {(ok ? "成功" : "失败")} {status}");
        return ok ? 0 : 1;
    }

    private static int RunSend(string[] a)
    {
        var m = AgentCore.NewReminder(
            GetOpt(a, "--type") ?? ReminderTypes.Info,
            GetOpt(a, "--project") ?? "",
            GetOpt(a, "--title") ?? "",
            GetOpt(a, "--detail") ?? "",
            GetOpt(a, "--session") ?? "",
            GetOpt(a, "--agent") ?? "manual",
            GetOpt(a, "--cwd") ?? Environment.CurrentDirectory);
        return AgentCore.SendAsync(m).GetAwaiter().GetResult().ok ? 0 : 1;
    }

    // Claude Code 钩子：无界面一次性进程，读 stdin → 发送 → 退出。
    private static int RunHook(string[] a)
    {
        string kind = a.Length > 1 ? a[1].ToLowerInvariant() : "";
        string stdin = ReadStdin();
        try
        {
            switch (kind)
            {
                case "claude-stop":
                {
                    var (s, c, _, transcript) = Hooks.ParseClaude(stdin);
                    string instruction = Hooks.ReadLastClaudeUserInstruction(transcript);
                    SendOrQueue(AgentCore.NewReminder(
                        ReminderTypes.TaskComplete, Hooks.ProjectFromCwd(c), "任务完成",
                        instruction, s, "claude_code", c));
                    break;
                }
                case "claude-notification":
                {
                    var (s, c, msg, transcript) = Hooks.ParseClaude(stdin);
                    string type = Hooks.ClassifyNotification(msg);
                    string title = type == ReminderTypes.NeedsApproval ? "需要授权" : "需要输入";
                    string instruction = Hooks.ReadLastClaudeUserInstruction(transcript);
                    if (instruction.Length == 0) instruction = msg;
                    SendOrQueue(AgentCore.NewReminder(type, Hooks.ProjectFromCwd(c), title, instruction, s, "claude_code", c));
                    break;
                }
                case "codex":
                {
                    string json = a.Length > 2 ? a[2] : stdin;
                    if (Hooks.IsCodexSubagent(json)) break;
                    var (s, c, last) = Hooks.ParseCodex(json);
                    SendOrQueue(AgentCore.NewReminder(
                        ReminderTypes.TaskComplete, Hooks.ProjectFromCwd(c), "Codex 回合完成", last, s, "codex", c));
                    break;
                }
                default:
                    return 2;
            }
        }
        catch (Exception e) { LogSilent("hook 失败: " + e.Message); }
        return 0; // hook 永不让代理失败
    }

    // 无托盘的纯后台监视（兼容旧自启/脚本；现默认改用 tray）。
    private static int RunWatchCli(string[] a)
    {
        ReminderSender sender;
        try { sender = new ReminderSender(); }
        catch { return 2; }
        using var cts = new CancellationTokenSource();
        new CodexRolloutWatcher(sender, GetOpt(a, "--dir")).RunAsync(cts.Token).GetAwaiter().GetResult();
        return 0;
    }

    // 自动触发（钩子）发送：受发送端类型过滤约束；失败则入本地兜底队列，托盘进程恢复连接后补发。
    private static void SendOrQueue(ReminderMessage m)
    {
        var (ok, status) = AgentCore.SendAsync(m, applyTypeFilter: true).GetAwaiter().GetResult();
        if (!ok && status != "type_disabled")
        {
            OutboxQueue.Enqueue(m);
            LogSilent("发送失败已入队（待恢复后补发）: " + status);
        }
    }

    private static string? GetOpt(string[] a, string name)
    {
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == name) return a[i + 1];
        return null;
    }

    private static string ReadStdin()
    {
        try
        {
            if (!Console.IsInputRedirected) return "";
            using var stdin = Console.OpenStandardInput();
            using var reader = new StreamReader(stdin, System.Text.Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch { return ""; }
    }

    private static void LogSilent(string msg)
    {
        try
        {
            Directory.CreateDirectory(SenderPaths.LogDir);
            File.AppendAllText(Path.Combine(SenderPaths.LogDir, $"agent-{DateTime.Now:yyyyMMdd}.log"),
                $"{DateTime.Now:HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch { /* 忽略 */ }
    }
}
