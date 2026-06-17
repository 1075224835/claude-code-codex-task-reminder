using System.IO;
using System.Text.Json;
using Reminder.Protocol.Dtos;
using Reminder.Receiver.Config;

namespace Reminder.Receiver.Stats;

/// <summary>
/// 消息类型统计（权威计数源，stats.json）。按 (did, type) 累计，附带 host 名。
/// PC 进程内直读快照；安卓经 GET /v1/stats 拉取。
/// </summary>
public sealed class StatsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly object _lock = new();
    private Dictionary<string, DeviceTypeCount> _records = new(); // key "did|type"

    public void Load()
    {
        lock (_lock)
        {
            _records = new();
            if (File.Exists(AppPaths.Stats))
            {
                var list = JsonSerializer.Deserialize<List<DeviceTypeCount>>(File.ReadAllText(AppPaths.Stats), JsonOpts) ?? new List<DeviceTypeCount>();
                foreach (var r in list) _records[r.Did + "|" + r.Type] = r;
            }
        }
    }

    public void Record(ReminderMessage m, string did)
    {
        lock (_lock)
        {
            string key = did + "|" + m.Type;
            if (_records.TryGetValue(key, out var existing))
                _records[key] = existing with { Count = existing.Count + 1, Host = string.IsNullOrEmpty(m.Host) ? existing.Host : m.Host };
            else
                _records[key] = new DeviceTypeCount { Did = did, Host = m.Host, Type = m.Type, Count = 1 };
            Save_NoLock();
        }
    }

    public StatsSnapshot Snapshot()
    {
        lock (_lock)
        {
            var totals = new Dictionary<string, long>();
            foreach (var r in _records.Values)
                totals[r.Type] = totals.GetValueOrDefault(r.Type) + r.Count;
            return new StatsSnapshot
            {
                GeneratedAt = DateTimeOffset.UtcNow.ToString("O"),
                Totals = totals,
                ByDevice = _records.Values.OrderByDescending(r => r.Count).ToList(),
            };
        }
    }

    private void Save_NoLock()
        => File.WriteAllText(AppPaths.Stats, JsonSerializer.Serialize(_records.Values.ToList(), JsonOpts));
}
