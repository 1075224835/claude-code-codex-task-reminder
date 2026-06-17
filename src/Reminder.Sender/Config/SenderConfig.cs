using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reminder.Sender.Config;

/// <summary>发送端本机配置（config.json）。密钥以平台方式保护落盘。</summary>
public sealed class SenderConfig
{
    [JsonPropertyName("hub")]    public string Hub { get; set; } = "";    // http://host:port
    [JsonPropertyName("kid")]    public string Kid { get; set; } = "";
    [JsonPropertyName("did")]    public string Did { get; set; } = "";
    [JsonPropertyName("msg_key")] public string MessageKeyProtected { get; set; } = "";
    [JsonPropertyName("token")]  public string TokenProtected { get; set; } = "";
    [JsonPropertyName("master")] public string MasterProtected { get; set; } = "";
    [JsonPropertyName("cert_thumbprint")] public string CertThumbprint { get; set; } = ""; // Hub 自签证书 SHA-256 指纹（pinning）
    /// <summary>本发送端「不发送」的消息类型（被用户勾掉的）。空=全部都发。自动触发(钩子/监视)受其约束，手动发送不受限。</summary>
    [JsonPropertyName("disabled_types")] public List<string> DisabledTypes { get; set; } = new();
}

public static class SenderPaths
{
    public static string Base { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "reminder-agent");

    public static string Config => Path.Combine(Base, "config.json");
    public static string LogDir => Path.Combine(Base, "logs");
}

public sealed class SenderConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public bool Exists => File.Exists(SenderPaths.Config);

    public SenderConfig Load()
    {
        if (!Exists)
            throw new InvalidOperationException("尚未配置。请先运行：reminder-agent enroll --code <配对码>");
        return JsonSerializer.Deserialize<SenderConfig>(File.ReadAllText(SenderPaths.Config), JsonOpts)
               ?? throw new InvalidOperationException("配置文件损坏。");
    }

    public void Save(SenderConfig cfg)
    {
        Directory.CreateDirectory(SenderPaths.Base);
        File.WriteAllText(SenderPaths.Config, JsonSerializer.Serialize(cfg, JsonOpts));
    }
}
