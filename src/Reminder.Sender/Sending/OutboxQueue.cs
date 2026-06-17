using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Reminder.Protocol.Dtos;
using Reminder.Sender.Config;

namespace Reminder.Sender.Sending;

/// <summary>
/// 本地兜底队列：Hub 不可达时把提醒暂存到磁盘(outbox.json)，连接恢复后补发。
/// 跨进程共享（钩子短进程入队，托盘长进程补发），文件锁内读改写，进程内 lock 串行。
/// </summary>
public static class OutboxQueue
{
    private static readonly object _lock = new();
    private const int MaxItems = 200;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private static string FilePath => Path.Combine(SenderPaths.Base, "outbox.json");

    public static void Enqueue(ReminderMessage m)
    {
        lock (_lock)
        {
            var list = LoadNoLock();
            list.Add(m);
            if (list.Count > MaxItems) list.RemoveRange(0, list.Count - MaxItems); // 超额丢最旧
            SaveNoLock(list);
        }
    }

    public static int Count
    {
        get { lock (_lock) return LoadNoLock().Count; }
    }

    /// <summary>尝试补发全部暂存项；成功的移除，失败的保留。返回成功补发条数。</summary>
    public static async Task<int> DrainAsync()
    {
        List<ReminderMessage> pending;
        lock (_lock) { pending = LoadNoLock(); }
        if (pending.Count == 0) return 0;

        ReminderSender sender;
        try { sender = new ReminderSender(); } catch { return 0; }

        int sent = 0;
        foreach (var m in pending)
        {
            bool ok;
            try { (ok, _) = await sender.SendAsync(m).ConfigureAwait(false); }
            catch { ok = false; }
            if (!ok) break; // 一旦失败(仍不可达)就停，剩下的下轮再试
            sent++;
        }

        int remaining = -1;
        if (sent > 0)
        {
            lock (_lock)
            {
                // 已成功的是最旧的 sent 条（按顺序发送、首个失败即止）；补发期间新入队的会追加在后，
                // 故按下标跳过前 sent 条即可——不依赖 ReminderMessage 的相等性（避免引用相等导致删不掉）。
                var current = LoadNoLock();
                var keep = current.Skip(System.Math.Min(sent, current.Count)).ToList();
                SaveNoLock(keep);
                remaining = LoadNoLock().Count;
            }
        }
        DLog($"补发 {sent}/{pending.Count} 条，剩余 {(remaining < 0 ? pending.Count - sent : remaining)} 条");
        return sent;
    }

    private static List<ReminderMessage> LoadNoLock()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<ReminderMessage>();
            return JsonSerializer.Deserialize<List<ReminderMessage>>(File.ReadAllText(FilePath), JsonOpts)
                   ?? new List<ReminderMessage>();
        }
        catch { return new List<ReminderMessage>(); }
    }

    private static void SaveNoLock(List<ReminderMessage> list)
    {
        try
        {
            Directory.CreateDirectory(SenderPaths.Base);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list, JsonOpts));
        }
        catch (System.Exception e) { DLog("save 失败: " + e.Message); }
    }

    private static void DLog(string m)
    {
        try
        {
            Directory.CreateDirectory(SenderPaths.LogDir);
            File.AppendAllText(Path.Combine(SenderPaths.LogDir, $"watch-{System.DateTime.Now:yyyyMMdd}.log"),
                $"[{System.DateTime.Now:HH:mm:ss}] outbox: {m}{System.Environment.NewLine}");
        }
        catch { /* 忽略 */ }
    }
}
