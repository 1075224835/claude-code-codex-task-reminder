using System.Security.Cryptography;
using Reminder.Protocol.Crypto;
using Reminder.Protocol.Dtos;
using Reminder.Protocol.Types;
using Reminder.Sender.Config;
using Reminder.Sender.Security;
using Reminder.Sender.Sending;
using Reminder.Sender.Transport;

namespace Reminder.Sender;

/// <summary>发送端共享逻辑：登记、构造提醒、发送。供 CLI 与托盘 GUI 共用。</summary>
public static class AgentCore
{
    public static bool IsEnrolled => new SenderConfigStore().Exists;

    /// <summary>用配对码登记到接收端（解码 ProvisioningBlob → 保护落盘）。</summary>
    public static void Enroll(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("配对码为空。请先在接收端「添加发送端」。");
        ProvisioningBlob blob;
        try { blob = ProvisioningBlob.Decode(code); }
        catch { throw new InvalidOperationException("配对码无效或不完整。请在接收端「添加发送端」后，完整复制配对码再粘贴。"); }
        if (string.IsNullOrEmpty(blob.Hub) || string.IsNullOrEmpty(blob.Did))
            throw new InvalidOperationException("配对码内容不完整，请重新从接收端复制。");
        if (blob.Exp > 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() > blob.Exp)
            throw new InvalidOperationException("配对码已过期。请在接收端重新「添加发送端」生成新码（有效期约 10 分钟）。");
        var enrollment = ResolveEnrollment(blob);
        var cfg = new SenderConfig
        {
            Hub = enrollment.Hub,
            Kid = enrollment.Kid,
            Did = blob.Did,
            MessageKeyProtected = Secret.Protect(enrollment.MessageKey),
            CertThumbprint = enrollment.CertThumbprint,
        };
        new SenderConfigStore().Save(cfg);
    }

    private static (byte[] MessageKey, string Hub, string Kid, string CertThumbprint) ResolveEnrollment(ProvisioningBlob blob)
    {
        if (!string.IsNullOrWhiteSpace(blob.EnrollSecret))
        {
            var client = new HubClient(blob.Hub, blob.CertThumbprint);
            var (ok, status, response) = client.EnrollAsync(new EnrollRequest
            {
                Did = blob.Did,
                Secret = blob.EnrollSecret,
            }).GetAwaiter().GetResult();
            if (!ok || response is null || string.IsNullOrWhiteSpace(response.MessageKey))
                throw new InvalidOperationException("在线登记失败：" + status);
            return (
                Convert.FromBase64String(response.MessageKey),
                string.IsNullOrWhiteSpace(response.Hub) ? blob.Hub : response.Hub,
                string.IsNullOrWhiteSpace(response.Kid) ? blob.Kid : response.Kid,
                string.IsNullOrWhiteSpace(response.CertThumbprint) ? blob.CertThumbprint : response.CertThumbprint);
        }
        if (!string.IsNullOrWhiteSpace(blob.MessageKey))
            return (Convert.FromBase64String(blob.MessageKey), blob.Hub, blob.Kid, blob.CertThumbprint);
        if (!string.IsNullOrWhiteSpace(blob.Token) && !string.IsNullOrWhiteSpace(blob.Master))
            return (KeyDerivation.DeriveMessageKey(
                Convert.FromBase64String(blob.Master),
                Convert.FromBase64String(blob.Token),
                blob.Did), blob.Hub, blob.Kid, blob.CertThumbprint);
        throw new InvalidOperationException("配对码缺少消息密钥，请在接收端重新生成。");
    }

    /// <summary>返回当前登记的接收端地址（用于显示）。未登记返回 null。</summary>
    public static string? HubAddress()
    {
        try { return new SenderConfigStore().Load().Hub; } catch { return null; }
    }

    /// <summary>心跳探测接收端是否可达（GET /v1/health，TLS 指纹 pinning）。未登记/异常返回 false。</summary>
    public static async Task<bool> PingAsync()
    {
        try
        {
            var c = new SenderConfigStore().Load();
            return await new HubClient(c.Hub, c.CertThumbprint).PingAsync();
        }
        catch { return false; }
    }

    public static ReminderMessage NewReminder(string type, string project, string title, string detail,
        string session, string agent, string cwd = "")
        => new()
        {
            Type = type,
            Host = Environment.MachineName,
            Project = project,
            Cwd = cwd,
            SessionId = session,
            Agent = agent,
            Title = title,
            Detail = detail,
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            Nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
        };

    public static Task<(bool ok, string status)> SendAsync(ReminderMessage m, bool applyTypeFilter = false)
        => new ReminderSender().SendAsync(m, applyTypeFilter);

    /// <summary>该类型是否启用自动发送（默认启用；在 DisabledTypes 中则禁用）。未登记时按启用。</summary>
    public static bool IsTypeEnabled(string type)
    {
        try { return !new SenderConfigStore().Load().DisabledTypes.Contains(type); }
        catch { return true; }
    }

    /// <summary>设置某类型是否自动发送，并落盘。</summary>
    public static void SetTypeEnabled(string type, bool enabled)
    {
        var store = new SenderConfigStore();
        var cfg = store.Load();
        if (enabled) cfg.DisabledTypes.Remove(type);
        else if (!cfg.DisabledTypes.Contains(type)) cfg.DisabledTypes.Add(type);
        store.Save(cfg);
    }

    public static Task<(bool ok, string status)> SendTestAsync()
        => SendAsync(NewReminder(ReminderTypes.NeedsInput, "测试项目", "测试提醒",
            "这是一条来自发送端的测试提醒。", "test-session", "manual"));
}
