using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Reminder.Sender;

/// <summary>把 Claude Code 的 Stop / Notification 钩子写入 ~/.claude/settings.json（保留已有钩子，幂等）。</summary>
public static class ClaudeHookInstaller
{
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static (bool ok, bool changed, string message) Install(string agentExe, string? settingsPath = null)
    {
        string path = settingsPath ?? SettingsPath;
        JsonObject root;
        try
        {
            if (File.Exists(path))
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                root = new JsonObject();
            }
        }
        catch (Exception e)
        {
            return (false, false, $"无法读取 ~/.claude/settings.json：{e.Message}");
        }

        if (root["hooks"] is not JsonObject hooks)
        {
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }

        bool changed = false;
        changed |= EnsureHook(hooks, "Stop", agentExe, "claude-stop", "全屏提醒：任务完成");
        changed |= EnsureHook(hooks, "Notification", agentExe, "claude-notification", "全屏提醒：需要处理");

        if (changed)
        {
            try { if (File.Exists(path)) File.Copy(path, path + ".bak", true); } catch { /* 忽略备份失败 */ }
            try { File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception e) { return (false, false, $"写入 settings.json 失败：{e.Message}"); }
        }

        return (true, changed, changed
            ? "已为 Claude Code 配置全屏提醒钩子。\r\n请重启 Claude Code（或在其中运行 /hooks 审核）后生效。"
            : "Claude Code 钩子已存在，无需重复配置。");
    }

    private static bool EnsureHook(JsonObject hooks, string evt, string agentExe, string kind, string statusMessage)
    {
        if (hooks[evt] is not JsonArray arr)
        {
            arr = new JsonArray();
            hooks[evt] = arr;
        }

        // 已有指向 reminder-agent 的钩子？存在则按需更新路径，避免重复。
        foreach (var grp in arr)
        {
            if (grp?["hooks"] is not JsonArray gh) continue;
            foreach (var h in gh)
            {
                string cmd = h?["command"]?.GetValue<string>() ?? "";
                if (cmd.Contains("reminder-agent", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(cmd, agentExe, StringComparison.OrdinalIgnoreCase))
                    {
                        h!["command"] = agentExe;
                        return true;
                    }
                    return false;
                }
            }
        }

        var newHook = new JsonObject
        {
            ["type"] = "command",
            ["command"] = agentExe,
            ["args"] = new JsonArray("hook", kind),
            ["timeout"] = 10,
            ["statusMessage"] = statusMessage,
        };
        arr.Add(new JsonObject { ["matcher"] = "", ["hooks"] = new JsonArray(newHook) });
        return true;
    }
}
