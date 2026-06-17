using System.Net.Http;
using Reminder.Protocol.Dtos;
using Reminder.Protocol.Types;
using Reminder.Receiver.Hub;
using Reminder.Receiver.Logging;

namespace Reminder.Receiver.Push;

/// <summary>
/// 经 Server酱（方糖 sctapi.ftqq.com）把提醒推到你的微信：无需装 App / 实名 / 审核 / 配对。
/// 用 SendKey 调 `https://sctapi.ftqq.com/{key}.send`（title + desp）。国内直连，微信收通知 + 铃声。
/// 注：免费版有每日条数配额；内容经方糖/微信服务器（非端到端）。
/// </summary>
public sealed class ServerChanForwarder : IRemoteForwarder
{
    private readonly string _sendKey;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public ServerChanForwarder(string sendKey) => _sendKey = sendKey;

    public void Forward(ReminderMessage msg)
    {
        string typeName = ReminderTypes.DefaultDisplayName(msg.Type);
        string title = BuildTitle(msg, typeName);
        string desp = BuildBody(msg);
        _ = PostAsync(title, desp, title);
    }

    /// <summary>Server酱 无远程撤回，ack 跨端取消留待后续。</summary>
    public void ForwardAck(string id) { }

    private async Task PostAsync(string title, string desp, string summary)
    {
        try
        {
            string url = $"https://sctapi.ftqq.com/{_sendKey}.send";
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["title"] = title,
                ["desp"] = desp,
                ["short"] = summary,
            });
            using var resp = await _http.PostAsync(url, content);
            string body = await resp.Content.ReadAsStringAsync();
            bool ok = resp.IsSuccessStatusCode && body.Contains("\"code\":0");
            if (!ok) Log.Warn($"Server酱 推送失败: {(int)resp.StatusCode} {body}");
        }
        catch (Exception e) { Log.Warn("Server酱 推送异常: " + e.Message); }
    }

    private static string BuildBody(ReminderMessage m)
    {
        string instruction = Trim(string.IsNullOrWhiteSpace(m.Detail) ? "未记录" : Clean(m.Detail), 800);
        string time = FormatTime(m.CreatedAt);
        string project = string.IsNullOrWhiteSpace(m.Project) ? "未记录" : m.Project;
        string path = string.IsNullOrWhiteSpace(m.Cwd) ? "未记录" : m.Cwd;

        var lines = new List<string>
        {
            "**最近指令**",
            "",
            instruction,
            "",
            "———",
            $"时间：{time}",
            $"项目：{project}",
            $"路径：{path}",
        };
        if (!string.IsNullOrWhiteSpace(m.Host)) lines.Add($"主机：{m.Host}");
        if (!string.IsNullOrWhiteSpace(m.SessionId)) lines.Add($"会话：{m.SessionId}");
        if (!string.IsNullOrWhiteSpace(m.Agent)) lines.Add($"来源：{m.Agent}");
        return string.Join("\n", lines);
    }

    private static string BuildTitle(ReminderMessage m, string typeName)
    {
        string task = !string.IsNullOrWhiteSpace(m.Project)
            ? m.Project
            : !string.IsNullOrWhiteSpace(m.Title)
                ? m.Title
                : m.Host;
        string title = $"{task} · {typeName}";
        const int max = 32; // 方糖标题上限约 32
        if (title.Length <= max) return title;
        if (typeName.Length + 3 >= max) return Trim(typeName, max);

        int taskMax = Math.Max(1, max - typeName.Length - 3);
        return $"{Trim(task, taskMax)} · {typeName}";
    }

    private static string FormatTime(string value)
    {
        if (DateTimeOffset.TryParse(value, out var dto))
            return dto.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        return string.IsNullOrWhiteSpace(value) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : value;
    }

    private static string Clean(string s)
        => s.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string Trim(string s, int max)
    {
        if (max <= 0) return "";
        if (s.Length <= max) return s;
        return max == 1 ? "…" : s[..(max - 1)] + "…";
    }
}
