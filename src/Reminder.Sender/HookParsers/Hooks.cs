using System.Text;
using System.Text.Json;
using Reminder.Protocol.Types;

namespace Reminder.Sender.HookParsers;

/// <summary>解析各代理的 hook/事件 JSON。容忍字段缺失/改名（隔离协议差异）。</summary>
public static class Hooks
{
    /// <summary>Claude Code hook stdin JSON → (session_id, cwd, message, transcript_path)。</summary>
    public static (string session, string cwd, string message, string transcriptPath) ParseClaude(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string transcript = Str(root, "transcript_path");
            if (transcript.Length == 0) transcript = Str(root, "transcriptPath");
            return (Str(root, "session_id"), Str(root, "cwd"), Str(root, "message"), transcript);
        }
        catch { return ("", "", "", ""); }
    }

    /// <summary>Codex notify/rollout JSON → (turn/thread id, cwd, recent user instruction or fallback message)。</summary>
    public static (string session, string cwd, string lastMessage) ParseCodex(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string s = Str(root, "turn-id");
            if (s.Length == 0) s = Str(root, "thread-id");
            string last = Str(root, "last-user-message");
            if (last.Length == 0) last = Str(root, "prompt");
            if (last.Length == 0) last = Str(root, "last-assistant-message");
            return (s, Str(root, "cwd"), last);
        }
        catch { return ("", "", ""); }
    }

    /// <summary>
    /// 把 Claude Code 的 Notification 消息细分为 needs_approval / needs_input。
    /// 权限提示典型为 "Claude needs your permission to use &lt;tool&gt;"；
    /// 空闲等待典型为 "Claude is waiting for your input"。无法判定时归为 needs_input。
    /// </summary>
    public static string ClassifyNotification(string message)
    {
        string m = message.ToLowerInvariant();
        if (m.Contains("permission") || m.Contains("approve") || m.Contains("approval") || m.Contains("授权") || m.Contains("权限"))
            return ReminderTypes.NeedsApproval;
        return ReminderTypes.NeedsInput;
    }

    /// <summary>从工作目录推断项目名（末段目录）。</summary>
    public static string ProjectFromCwd(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return "";
        string trimmed = cwd.TrimEnd('/', '\\');
        int i = trimmed.LastIndexOfAny(new[] { '/', '\\' });
        return i >= 0 ? trimmed[(i + 1)..] : trimmed;
    }

    public static string ReadLastClaudeUserInstruction(string transcriptPath)
    {
        if (string.IsNullOrWhiteSpace(transcriptPath) || !File.Exists(transcriptPath)) return "";

        string last = "";
        try
        {
            foreach (var line in ReadTailLines(transcriptPath, 500))
            {
                string text = TryExtractClaudeUserInstruction(line);
                if (text.Length > 0) last = text;
            }
        }
        catch { return ""; }
        return Limit(last, 800);
    }

    public static string ExtractUserContentText(JsonElement content)
        => Limit(ExtractContentText(content, skipToolResults: true), 800);

    private static IEnumerable<string> ReadTailLines(string path, int maxLines)
    {
        var q = new Queue<string>(maxLines);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            q.Enqueue(line);
            if (q.Count > maxLines) q.Dequeue();
        }
        return q.ToArray();
    }

    private static string TryExtractClaudeUserInstruction(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (Str(root, "type") != "user") return "";
            if (!root.TryGetProperty("message", out var msg)) return "";
            if (Str(msg, "role") != "user") return "";
            if (!msg.TryGetProperty("content", out var content)) return "";

            string text = ExtractUserContentText(content);
            return LooksSyntheticUserText(text) ? "" : text;
        }
        catch { return ""; }
    }

    private static string ExtractContentText(JsonElement content, bool skipToolResults)
    {
        if (content.ValueKind == JsonValueKind.String)
            return Normalize(content.GetString() ?? "");

        var sb = new StringBuilder();
        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
                AppendContentText(sb, item, skipToolResults);
        }
        else
        {
            AppendContentText(sb, content, skipToolResults);
        }
        return Normalize(sb.ToString());
    }

    private static void AppendContentText(StringBuilder sb, JsonElement item, bool skipToolResults)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            AppendPart(sb, item.GetString() ?? "");
            return;
        }
        if (item.ValueKind != JsonValueKind.Object) return;

        string type = Str(item, "type");
        if (skipToolResults && type == "tool_result") return;

        if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            AppendPart(sb, text.GetString() ?? "");

        if (item.TryGetProperty("content", out var nested))
            AppendPart(sb, ExtractContentText(nested, skipToolResults));
    }

    private static void AppendPart(StringBuilder sb, string text)
    {
        text = Normalize(text);
        if (text.Length == 0) return;
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(text);
    }

    private static bool LooksSyntheticUserText(string text)
    {
        string t = text.TrimStart();
        return t.StartsWith("<task-notification>", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("<system-reminder>", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string text)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (text.Contains("  ")) text = text.Replace("  ", " ");
        return text;
    }

    private static string Limit(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
