using Reminder.Protocol.Crypto;
using Reminder.Protocol.Dtos;
using Reminder.Sender.Config;
using Reminder.Sender.Security;
using Reminder.Sender.Transport;

namespace Reminder.Sender.Sending;

/// <summary>加载本机配置、派生密钥，把 ReminderMessage 加密后发往 Hub。可复用（hook 与 watch 共用）。</summary>
public sealed class ReminderSender
{
    private readonly SenderConfig _cfg;
    private readonly byte[] _key;
    private readonly HubClient _client;

    public ReminderSender()
    {
        _cfg = new SenderConfigStore().Load();
        if (!string.IsNullOrWhiteSpace(_cfg.MessageKeyProtected))
        {
            _key = Secret.Unprotect(_cfg.MessageKeyProtected);
        }
        else
        {
            byte[] master = Secret.Unprotect(_cfg.MasterProtected);
            byte[] token = Secret.Unprotect(_cfg.TokenProtected);
            _key = KeyDerivation.DeriveMessageKey(master, token, _cfg.Did);
        }
        _client = new HubClient(_cfg.Hub, _cfg.CertThumbprint);
    }

    public async Task<(bool ok, string status)> SendAsync(ReminderMessage m, bool applyTypeFilter = false)
    {
        if (applyTypeFilter && _cfg.DisabledTypes.Contains(m.Type))
            return (true, "type_disabled"); // 该类型被发送端设为不发送（仅约束自动触发）
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var env = EnvelopeCrypto.Encrypt(m, _cfg.Kid, _cfg.Did, _key, ts);
        return await _client.SendReminder(env).ConfigureAwait(false);
    }
}
