namespace Reminder.Protocol.Crypto;

/// <summary>
/// 防重放/防过期守卫：拒绝时间戳超窗的消息，并对窗口内的 (did, nonce) 去重。
/// 线程安全。内存态即可（单用户场景，重启清空可接受）。
/// </summary>
public sealed class ReplayGuard
{
    private readonly long _windowSec;
    private readonly object _lock = new();
    private readonly Dictionary<string, long> _seen = new(); // "did|nonce" -> 收到时的 unix 秒

    public ReplayGuard(int? windowSeconds = null)
        => _windowSec = windowSeconds ?? ProtocolConstants.FreshnessWindowSeconds;

    public bool IsFresh(long ts, long nowUnix, out string reason)
    {
        if (Math.Abs(nowUnix - ts) > _windowSec)
        {
            reason = $"时间戳超出 ±{_windowSec}s 新鲜度窗口";
            return false;
        }
        reason = "";
        return true;
    }

    public bool Remember(string did, byte[] nonce, long nowUnix, out string reason)
        => RememberCanonical(did, Convert.ToBase64String(nonce), nowUnix, out reason);

    /// <summary>校验一条消息是否可接受。nowUnix 由调用方传入便于测试。</summary>
    public bool Check(string did, string nonce, long ts, long nowUnix, out string reason)
    {
        if (!IsFresh(ts, nowUnix, out reason)) return false;
        return RememberCanonical(did, nonce, nowUnix, out reason);
    }

    private bool RememberCanonical(string did, string nonce, long nowUnix, out string reason)
    {
        string key = did + "|" + nonce;
        lock (_lock)
        {
            Prune(nowUnix);
            if (_seen.ContainsKey(key))
            {
                reason = "重放/重复消息";
                return false;
            }
            _seen[key] = nowUnix;
        }

        reason = "";
        return true;
    }

    private void Prune(long nowUnix)
    {
        long cutoff = nowUnix - _windowSec * 2;
        if (_seen.Count == 0) return;
        var dead = _seen.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
        foreach (var k in dead) _seen.Remove(k);
    }
}
