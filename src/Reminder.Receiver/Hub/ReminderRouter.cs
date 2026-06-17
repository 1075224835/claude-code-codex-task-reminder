using Reminder.Protocol;
using Reminder.Protocol.Crypto;
using Reminder.Protocol.Dtos;
using Reminder.Receiver.Config;
using Reminder.Receiver.Logging;
using Reminder.Receiver.Prefs;
using Reminder.Receiver.Stats;

namespace Reminder.Receiver.Hub;

public readonly record struct RouteResult(int Code, string Status);

/// <summary>
/// 消息路由核心：解密 → 校验（设备/重放）→ 去重 → 计数 → 按类型过滤 → 本地展示（+ 后续 FCM 扇出）。
/// 不依赖 Kestrel/WPF，便于无 GUI 集成测试。
/// </summary>
public sealed class ReminderRouter
{
    private readonly ConfigManager _config;
    private readonly DeviceRegistry _registry;
    private readonly ReplayGuard _replay;
    private readonly StatsStore _stats;
    private readonly IReminderBus _bus;
    private readonly PrefsStore _prefs;
    private readonly IRemoteForwarder? _forwarder;

    public ReminderRouter(ConfigManager config, DeviceRegistry registry, ReplayGuard replay,
        StatsStore stats, IReminderBus bus, PrefsStore prefs, IRemoteForwarder? forwarder = null)
    {
        _config = config; _registry = registry; _replay = replay;
        _stats = stats; _bus = bus; _prefs = prefs; _forwarder = forwarder;
    }

    public RouteResult Ingest(EncryptedEnvelope env)
    {
        if (!_registry.TryGetMessageKey(env.Did, _config.MasterKey, out var key))
        {
            Log.Warn($"拒绝：未知/已撤销设备 {env.Did}");
            return new RouteResult(401, "unknown_device");
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!_registry.CheckFirstContact(env.Did, now, out var creason))
        {
            Log.Warn($"拒绝：{creason}（设备 {env.Did}，配对码时效）");
            return new RouteResult(403, creason);
        }
        if (!_replay.IsFresh(env.Ts, now, out var reason))
        {
            Log.Warn($"拒绝：{reason}（设备 {env.Did}）");
            return new RouteResult(409, "replay_or_stale");
        }
        byte[] nonce;
        try { nonce = EnvelopeCrypto.DecodeCanonicalBase64(env.Nonce, ProtocolConstants.GcmNonceBytes, "nonce"); }
        catch (FormatException e) { Log.Warn($"拒绝：nonce 非法（设备 {env.Did}）：{e.Message}"); return new RouteResult(400, "bad_nonce"); }

        ReminderMessage msg;
        try { msg = EnvelopeCrypto.Decrypt<ReminderMessage>(env, key); }
        catch (Exception e) { Log.Warn($"拒绝：解密失败 {e.GetType().Name}（设备 {env.Did}）"); return new RouteResult(400, "decrypt_failed"); }
        if (!_replay.Remember(env.Did, nonce, now, out reason))
        {
            Log.Warn($"拒绝：{reason}（设备 {env.Did}）");
            return new RouteResult(409, "replay_or_stale");
        }
        _registry.ConfirmFirstContact(env.Did);
        _registry.Touch(env.Did, msg.Host);

        _stats.Record(msg, env.Did);

        var pref = _prefs.Prefs.ForType(msg.Type);
        if (pref.Enabled)
            _bus.PublishReminder(msg);
        else
            Log.Info($"类型 {msg.Type} 被本机设为忽略，仅计数（来自 {msg.Host}）");

        _forwarder?.Forward(msg); // FCM 扇出（P5），现在为 null
        Log.Info($"提醒 {msg.Type} 来自 {msg.Host}/{msg.Project} 已路由");
        return new RouteResult(200, "ok");
    }

    public RouteResult IngestAck(EncryptedEnvelope env)
    {
        if (!_registry.TryGetMessageKey(env.Did, _config.MasterKey, out var key))
            return new RouteResult(401, "unknown_device");
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!_registry.CheckFirstContact(env.Did, now, out var creason))
            return new RouteResult(403, creason);
        if (!_replay.IsFresh(env.Ts, now, out _))
            return new RouteResult(409, "replay_or_stale");
        byte[] nonce;
        try { nonce = EnvelopeCrypto.DecodeCanonicalBase64(env.Nonce, ProtocolConstants.GcmNonceBytes, "nonce"); }
        catch { return new RouteResult(400, "bad_nonce"); }
        AckMessage ack;
        try { ack = EnvelopeCrypto.Decrypt<AckMessage>(env, key); }
        catch { return new RouteResult(400, "decrypt_failed"); }
        if (!_replay.Remember(env.Did, nonce, now, out _))
            return new RouteResult(409, "replay_or_stale");
        _registry.ConfirmFirstContact(env.Did);
        _registry.Touch(env.Did);
        _bus.PublishAck(ack.Id);
        _forwarder?.ForwardAck(ack.Id);
        return new RouteResult(200, "ok");
    }
}

/// <summary>远程扇出抽象（P5 由 FCM 实现）。</summary>
public interface IRemoteForwarder
{
    void Forward(ReminderMessage msg);
    void ForwardAck(string id);
}
