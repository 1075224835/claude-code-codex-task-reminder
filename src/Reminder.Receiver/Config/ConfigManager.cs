using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Reminder.Protocol;
using Reminder.Protocol.Crypto;
using Reminder.Receiver.Logging;
using Reminder.Receiver.Security;

namespace Reminder.Receiver.Config;

/// <summary>接收端核心配置（config.json）。主密钥以 DPAPI 包裹后落盘。</summary>
public sealed class ReceiverConfig
{
    [JsonPropertyName("kid")]                 public string Kid { get; set; } = "ws1";
    [JsonPropertyName("master_key_protected")] public string MasterKeyProtected { get; set; } = "";
    [JsonPropertyName("hub_port")]            public int HubPort { get; set; } = 8740;
    [JsonPropertyName("scheme")]              public string Scheme { get; set; } = "https"; // 自签 TLS + 指纹 pinning
    [JsonPropertyName("cert_pfx_protected")]  public string CertPfxProtected { get; set; } = "";
    [JsonPropertyName("cert_thumbprint")]     public string CertThumbprint { get; set; } = "";
    /// <summary>手动指定发送端连接用的地址/IP（覆盖自动探测）。留空=自动选真实 LAN 网卡。</summary>
    [JsonPropertyName("advertise_host")]      public string? AdvertiseHost { get; set; }
    // Server酱（方糖）：SendKey（DPAPI 包裹），推送到微信。
    [JsonPropertyName("serverchan_key_protected")]     public string? ServerChanKeyProtected { get; set; }
}

/// <summary>加载/保存配置；首次运行自动生成主密钥。内存中持有解包后的主密钥。</summary>
public sealed class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ReceiverConfig Config { get; private set; } = new();
    public byte[] MasterKey { get; private set; } = Array.Empty<byte>();
    public X509Certificate2 ServerCert { get; private set; } = null!;

    public void Load()
    {
        AppPaths.EnsureBase();
        if (File.Exists(AppPaths.Config))
        {
            Config = JsonSerializer.Deserialize<ReceiverConfig>(File.ReadAllText(AppPaths.Config), JsonOpts) ?? new();
            if (string.IsNullOrEmpty(Config.MasterKeyProtected))
            {
                MasterKey = KeyDerivation.NewMasterKey();
                Config.MasterKeyProtected = Dpapi.Protect(MasterKey);
                Save();
            }
            else
            {
                MasterKey = Dpapi.Unprotect(Config.MasterKeyProtected);
            }
            Log.Info("已加载配置。");
        }
        else
        {
            MasterKey = KeyDerivation.NewMasterKey();
            Config = new ReceiverConfig { MasterKeyProtected = Dpapi.Protect(MasterKey) };
            Save();
            Log.Info("首次运行：已生成主密钥并写入配置。");
        }

        EnsureCert();
    }

    /// <summary>确保自签 TLS 证书存在并加载。指纹一律用 SHA-256（自愈旧的 SHA-1 配置）。</summary>
    private void EnsureCert()
    {
        bool dirty = false;
        if (string.IsNullOrEmpty(Config.CertPfxProtected))
        {
            byte[] pfx = SelfSignedCert.GeneratePfx();
            ServerCert = SelfSignedCert.LoadServerCert(pfx);
            Config.CertPfxProtected = Dpapi.Protect(pfx);
            Config.Scheme = "https";
            dirty = true;
            Log.Info("已生成自签 TLS 证书");
        }
        else
        {
            byte[] pfx = Dpapi.Unprotect(Config.CertPfxProtected);
            ServerCert = SelfSignedCert.LoadServerCert(pfx);
        }

        string fp = SelfSignedCert.Sha256Thumbprint(ServerCert);
        if (!string.Equals(Config.CertThumbprint, fp, StringComparison.OrdinalIgnoreCase))
        {
            Config.CertThumbprint = fp; // 旧 SHA-1 指纹会在此被替换为 SHA-256
            dirty = true;
            Log.Info($"TLS 证书 SHA-256 指纹：{fp}");
        }
        if (dirty) Save();
    }

    public void Save() => File.WriteAllText(AppPaths.Config, JsonSerializer.Serialize(Config, JsonOpts));
}
