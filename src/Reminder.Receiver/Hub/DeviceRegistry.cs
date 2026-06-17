using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Reminder.Protocol;
using Reminder.Protocol.Crypto;
using Reminder.Receiver.Config;
using Reminder.Receiver.Security;

namespace Reminder.Receiver.Hub;

/// <summary>一台已登记设备。设备 token 以 DPAPI 包裹落盘。</summary>
public sealed class Device
{
    [JsonPropertyName("did")]             public string Did { get; set; } = "";
    [JsonPropertyName("label")]           public string Label { get; set; } = "";
    [JsonPropertyName("kind")]            public string Kind { get; set; } = "sender";
    [JsonPropertyName("token_protected")] public string TokenProtected { get; set; } = "";
    [JsonPropertyName("enroll_secret_protected")] public string EnrollSecretProtected { get; set; } = "";
    [JsonPropertyName("revoked")]         public bool Revoked { get; set; }
    [JsonPropertyName("created_at")]      public string CreatedAt { get; set; } = "";
    [JsonPropertyName("last_seen_at")]    public string LastSeenAt { get; set; } = "";
    [JsonPropertyName("last_host")]       public string LastHost { get; set; } = "";
    /// <summary>配对码时效：未确认设备须在此 unix 秒前首次连接，否则作废。0=无限制。</summary>
    [JsonPropertyName("enroll_by")]       public long EnrollBy { get; set; }
    /// <summary>是否已首次成功连接确认（确认后 EnrollBy 不再生效）。</summary>
    [JsonPropertyName("confirmed")]       public bool Confirmed { get; set; }
}

/// <summary>新登记设备的结果，含接收端生成的临时机密（仅用于生成短期配对码）。</summary>
public sealed record NewDevice(Device Device, byte[] Token, byte[] EnrollSecret);

/// <summary>设备注册表（devices.json）。线程安全。</summary>
public sealed class DeviceRegistry
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly object _lock = new();
    private List<Device> _devices = new();

    public void Load()
    {
        lock (_lock)
        {
            _devices = File.Exists(AppPaths.Devices)
                ? JsonSerializer.Deserialize<List<Device>>(File.ReadAllText(AppPaths.Devices), JsonOpts) ?? new()
                : new();
        }
    }

    public IReadOnlyList<Device> All
    {
        get { lock (_lock) return _devices.ToList(); }
    }

    public NewDevice Add(string label, string kind, long enrollBy = 0)
    {
        byte[] token = KeyDerivation.NewDeviceToken();
        byte[] enrollSecret = RandomNumberGenerator.GetBytes(ProtocolConstants.KeyBytes);
        var dev = new Device
        {
            Did = "d-" + Guid.NewGuid().ToString("N")[..12],
            Label = label,
            Kind = kind,
            TokenProtected = Dpapi.Protect(token),
            EnrollSecretProtected = Dpapi.Protect(enrollSecret),
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            EnrollBy = enrollBy,
        };
        lock (_lock) { _devices.Add(dev); Save_NoLock(); }
        return new NewDevice(dev, token, enrollSecret);
    }

    public bool TryConsumeEnrollment(string did, byte[] secret, byte[] masterKey, out byte[] msgKey, out string reason)
    {
        msgKey = Array.Empty<byte>();
        reason = "";
        lock (_lock)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var d = _devices.FirstOrDefault(x => x.Did == did && !x.Revoked);
            if (d is null) { reason = "unknown_device"; return false; }
            if (d.Confirmed) { reason = "already_enrolled"; return false; }
            if (d.EnrollBy > 0 && now > d.EnrollBy)
            {
                d.Revoked = true;
                Save_NoLock();
                reason = "enroll_expired";
                return false;
            }
            if (string.IsNullOrWhiteSpace(d.EnrollSecretProtected))
            {
                reason = "enroll_secret_missing";
                return false;
            }

            byte[] expected = Dpapi.Unprotect(d.EnrollSecretProtected);
            if (!CryptographicOperations.FixedTimeEquals(expected, secret))
            {
                reason = "bad_secret";
                return false;
            }

            byte[] token = Dpapi.Unprotect(d.TokenProtected);
            msgKey = KeyDerivation.DeriveMessageKey(masterKey, token, did);
            d.EnrollSecretProtected = "";
            d.Confirmed = true;
            Save_NoLock();
            return true;
        }
    }

    /// <summary>配对码时效检查：未确认的设备必须在 EnrollBy 之前首次连接，逾期则撤销并拒绝。</summary>
    public bool CheckFirstContact(string did, long nowUnix, out string reason)
    {
        reason = "";
        lock (_lock)
        {
            var d = _devices.FirstOrDefault(x => x.Did == did && !x.Revoked);
            if (d is null) { reason = "unknown_device"; return false; }
            if (d.Confirmed) return true;
            if (d.EnrollBy > 0 && nowUnix > d.EnrollBy)
            {
                d.Revoked = true;
                Save_NoLock();
                reason = "enroll_expired";
                return false;
            }
            return true;
        }
    }

    /// <summary>认证解密成功后确认首次连接。只有有效消息可消费配对窗口。</summary>
    public void ConfirmFirstContact(string did)
    {
        lock (_lock)
        {
            var d = _devices.FirstOrDefault(x => x.Did == did && !x.Revoked);
            if (d is null || d.Confirmed) return;
            d.Confirmed = true;
            Save_NoLock();
        }
    }

    public void Touch(string did, string? host = null)
    {
        lock (_lock)
        {
            var d = _devices.FirstOrDefault(x => x.Did == did && !x.Revoked);
            if (d is null) return;

            d.LastSeenAt = DateTimeOffset.UtcNow.ToString("O");
            if (!string.IsNullOrWhiteSpace(host))
                d.LastHost = host.Trim();
            Save_NoLock();
        }
    }

    public Device? Find(string did)
    {
        lock (_lock) return _devices.FirstOrDefault(d => d.Did == did);
    }

    /// <summary>查找设备并派生其消息密钥。被撤销或不存在返回 false。</summary>
    public bool TryGetMessageKey(string did, byte[] masterKey, out byte[] key)
    {
        key = Array.Empty<byte>();
        Device? dev;
        lock (_lock) dev = _devices.FirstOrDefault(d => d.Did == did && !d.Revoked);
        if (dev is null) return false;
        byte[] token = Dpapi.Unprotect(dev.TokenProtected);
        key = KeyDerivation.DeriveMessageKey(masterKey, token, did);
        return true;
    }

    public void Revoke(string did)
    {
        lock (_lock)
        {
            var d = _devices.FirstOrDefault(x => x.Did == did);
            if (d is not null) { d.Revoked = true; Save_NoLock(); }
        }
    }

    public void Update(Device dev)
    {
        lock (_lock)
        {
            int i = _devices.FindIndex(x => x.Did == dev.Did);
            if (i >= 0) _devices[i] = dev; else _devices.Add(dev);
            Save_NoLock();
        }
    }

    private void Save_NoLock()
        => File.WriteAllText(AppPaths.Devices, JsonSerializer.Serialize(_devices, JsonOpts));
}
