using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Reminder.Protocol.Dtos;
using Reminder.Protocol.Types;
using Reminder.Sender.Config;
using Reminder.Sender.HookParsers;
using Reminder.Sender.Sending;

namespace Reminder.Sender.Watchers;

/// <summary>
/// 监视 Codex 桌面应用的会话 rollout 日志（~/.codex/sessions/**/rollout-*.jsonl），
/// 检测 event_msg/task_complete → 发出 task_complete 提醒。轮询式 tail，对 Defender 占用健壮。
///
/// 关键事件（已实测）：
///   session_meta            → payload.{id, cwd}
///   event_msg/agent_message → payload.message（最近一条最终回答，作 detail 兜底）
///   event_msg/task_complete → payload.{turn_id, last_agent_message}（回合完成）
/// Codex 无明确「等待审批」rollout 事件，故 needs_input 不在此可靠捕获。
/// </summary>
public sealed class CodexRolloutWatcher
{
    private readonly ReminderSender _sender;
    private readonly string _root;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(2);
    private readonly Dictionary<string, FileState> _state = new();

    private sealed class FileState
    {
        public long Offset;
        public List<byte> Leftover = new();
        public string SessionId = "";
        public string Cwd = "";
        public string LastAgentMsg = "";
        public string LastUserInstruction = "";
        public bool MetaRead;
    }

    public CodexRolloutWatcher(ReminderSender sender, string? root = null)
    {
        _sender = sender;
        _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_root))
        {
            Write($"未找到 Codex 会话目录：{_root}");
            return;
        }
        Seed();
        Write($"监视中：{_root}（已跳过历史，仅捕获新的回合完成）");

        while (!ct.IsCancellationRequested)
        {
            try { PollOnce(); }
            catch (Exception e) { Log("poll 失败: " + e.Message); }
            try { await Task.Delay(_interval, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private IEnumerable<string> EnumerateRollouts()
    {
        try { return Directory.EnumerateFiles(_root, "rollout-*.jsonl", SearchOption.AllDirectories); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>启动时把已有文件 offset 设为当前长度，跳过历史回合。</summary>
    private void Seed()
    {
        foreach (var path in EnumerateRollouts())
        {
            try { _state[path] = new FileState { Offset = new FileInfo(path).Length }; }
            catch { /* 忽略 */ }
        }
    }

    // 单文件超过此大小则不读取其内容：避免 Windows Defender 实时扫描超大 rollout 文件导致系统卡顿。
    private const long MaxReadableBytes = 60L * 1024 * 1024;
    private bool _warnedLarge;

    private void PollOnce()
    {
        foreach (var path in EnumerateRollouts())
        {
            long len;
            try { len = new FileInfo(path).Length; }
            catch { continue; }

            if (!_state.TryGetValue(path, out var st))
            {
                st = new FileState { Offset = 0 }; // 监视期间新建的会话 → 从头读
                _state[path] = st;
            }

            // 超大会话文件：仅记录到末尾、绝不打开读取（防 Defender 扫描大文件卡顿）
            if (len > MaxReadableBytes)
            {
                st.Offset = len;
                st.Leftover.Clear();
                if (!_warnedLarge)
                {
                    _warnedLarge = true;
                    Write("检测到超大会话文件(>60MB)，已跳过以避免卡顿。如需监视大会话，请把 ~/.codex/sessions 加入 Defender 排除项（发送端托盘可一键添加）。");
                }
                continue;
            }

            if (len < st.Offset) { st.Offset = 0; st.Leftover.Clear(); } // 截断/轮换
            else if (len == st.Offset) continue;                          // 无新数据

            ReadNew(path, st);
        }
    }

    private void ReadNew(string path, FileState st)
    {
        byte[] chunk;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long len = fs.Length;
            if (len <= st.Offset) return;
            fs.Seek(st.Offset, SeekOrigin.Begin);
            int toRead = (int)Math.Min(len - st.Offset, 4 * 1024 * 1024); // 单次最多 4MB
            chunk = new byte[toRead];
            int read = fs.Read(chunk, 0, toRead);
            if (read <= 0) return;
            if (read < toRead) Array.Resize(ref chunk, read);
            st.Offset += read;
        }
        catch (Exception e)
        {
            Log($"读取失败（可能被 Defender 占用，将重试）：{Path.GetFileName(path)} {e.Message}");
            return;
        }

        // leftover + chunk，按最后一个换行切分，余下保留
        var combined = new byte[st.Leftover.Count + chunk.Length];
        st.Leftover.CopyTo(combined, 0);
        Array.Copy(chunk, 0, combined, st.Leftover.Count, chunk.Length);

        int lastNl = Array.LastIndexOf(combined, (byte)'\n');
        if (lastNl < 0) { st.Leftover = new List<byte>(combined); return; }

        string text = Encoding.UTF8.GetString(combined, 0, lastNl);
        st.Leftover = combined.Skip(lastNl + 1).ToList();

        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            try { ProcessLine(path, st, line); }
            catch { /* 跳过损坏/非 JSON 行 */ }
        }
    }

    private void ProcessLine(string path, FileState st, string json)
    {
        if (json.Length > 0 && json[0] == '﻿') json = json[1..]; // 容忍首行 BOM
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeEl)) return;
        string type = typeEl.GetString() ?? "";

        if (type == "session_meta")
        {
            if (root.TryGetProperty("payload", out var pl))
            {
                st.SessionId = Str(pl, "id");
                st.Cwd = Str(pl, "cwd");
                st.MetaRead = true;
            }
            return;
        }

        if (type == "response_item" && root.TryGetProperty("payload", out var responsePayload))
        {
            RememberUserInstruction(st, responsePayload);
            return;
        }

        if (type != "event_msg" || !root.TryGetProperty("payload", out var p)) return;
        string et = Str(p, "type");

        if (et == "agent_message")
        {
            string msg = Str(p, "message");
            if (msg.Length > 0) st.LastAgentMsg = msg;
        }
        else if (et == "task_complete")
        {
            if (!st.MetaRead) TryReadMeta(path, st);
            string detail = Str(p, "last_agent_message");
            if (detail.Length == 0) detail = st.LastAgentMsg;
            SendTaskComplete(st, detail);
            st.LastAgentMsg = "";
        }
    }

    /// <summary>task_complete 时若尚未读到 session_meta（如新文件竞态），补读首行。</summary>
    private void TryReadMeta(string path, FileState st)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            string? first = sr.ReadLine();
            if (first is null) return;
            using var doc = JsonDocument.Parse(first);
            if (doc.RootElement.TryGetProperty("payload", out var pl))
            {
                st.SessionId = Str(pl, "id");
                st.Cwd = Str(pl, "cwd");
                st.MetaRead = true;
            }
        }
        catch { /* 忽略 */ }
    }

    private void SendTaskComplete(FileState st, string detail)
    {
        string project = Hooks.ProjectFromCwd(st.Cwd);
        if (detail.Length > 400) detail = detail[..400] + "…";
        string instruction = st.LastUserInstruction.Length > 0 ? st.LastUserInstruction : detail;
        if (instruction.Length > 800) instruction = instruction[..800] + "…";

        var m = new ReminderMessage
        {
            Type = ReminderTypes.TaskComplete,
            Host = Environment.MachineName,
            Project = project,
            Cwd = st.Cwd,
            SessionId = st.SessionId,
            Agent = "codex",
            Title = "Codex 任务完成",
            Detail = instruction,
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            Nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
        };

        // 非阻塞发送：即使 Hub 暂不可达也不卡住轮询循环；失败则入本地兜底队列，恢复后补发。
        _ = _sender.SendAsync(m, applyTypeFilter: true).ContinueWith(t =>
        {
            bool done = t.Status == TaskStatus.RanToCompletion;
            bool ok = done && t.Result.ok;
            string status = done ? t.Result.status : "异常";
            if (!ok && status != "type_disabled") OutboxQueue.Enqueue(m);
            Write($"Codex 回合完成 → {(project.Length > 0 ? project : "(未知项目)")} … {(ok ? "已发送" : status == "type_disabled" ? "类型已忽略" : "失败已入队: " + status)}");
        });
    }

    private static void RememberUserInstruction(FileState st, JsonElement payload)
    {
        if (Str(payload, "type") != "message") return;
        if (Str(payload, "role") != "user") return;
        if (!payload.TryGetProperty("content", out var content)) return;

        string text = Hooks.ExtractUserContentText(content);
        if (text.Length > 0) st.LastUserInstruction = text;
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static void Log(string m) => Write(m);

    /// <summary>同时输出到控制台与日志文件（隐藏后台运行时便于排查）。</summary>
    private static void Write(string m)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {m}";
        Console.WriteLine(line);
        try
        {
            Directory.CreateDirectory(SenderPaths.LogDir);
            File.AppendAllText(Path.Combine(SenderPaths.LogDir, $"watch-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
        }
        catch { /* 忽略 */ }
    }
}
