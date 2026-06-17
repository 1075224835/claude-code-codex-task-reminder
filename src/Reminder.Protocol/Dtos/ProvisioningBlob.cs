using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Reminder.Protocol.Dtos;

/// <summary>
/// 配对码内容：接收端「添加设备」时生成，经二维码/base64 文本交给发送端或手机。
/// ⚠️ 含一次性登记密钥，属短时机密，仅在可信接收端屏幕短暂展示。
/// </summary>
public sealed record ProvisioningBlob
{
    [JsonPropertyName("hub")]    public string Hub { get; init; } = "";    // 如 http://192.168.1.10:8740
    [JsonPropertyName("kid")]    public string Kid { get; init; } = "";
    [JsonPropertyName("did")]    public string Did { get; init; } = "";
    [JsonPropertyName("enroll_secret")] public string? EnrollSecret { get; init; } // 新格式：base64 一次性登记密钥
    [JsonPropertyName("msg_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? MessageKey { get; init; } // 过渡格式兼容：base64 每设备消息密钥
    [JsonPropertyName("token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Token { get; init; }  // 旧格式兼容：base64 设备 token
    [JsonPropertyName("master")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Master { get; init; } // 旧格式兼容：base64 主密钥
    [JsonPropertyName("cert_thumbprint")] public string CertThumbprint { get; init; } = ""; // Hub 自签证书 SHA-256 指纹（pinning）
    [JsonPropertyName("kind")]   public string Kind { get; init; } = "sender";
    [JsonPropertyName("exp")]    public long Exp { get; init; } // 配对码过期时间（unix 秒）；0=不过期

    /// <summary>编码为可粘贴/扫码的 base64(JSON) 配对码。</summary>
    public string Encode()
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(ProtocolJson.ToJson(this)));

    /// <summary>
    /// 从配对码解析。容错：忽略所有空白（换行/空格/制表）；若用户把整段说明都粘进来，
    /// 自动提取其中最长的 base64 片段。
    /// </summary>
    public static ProvisioningBlob Decode(string code)
    {
        string compact = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
        string b64 = IsBase64(compact) ? compact : LongestBase64Run(compact);
        if (b64.Length == 0) throw new FormatException("未找到有效的配对码内容。");
        byte[] bytes = Convert.FromBase64String(b64);
        return ProtocolJson.FromJson<ProvisioningBlob>(Encoding.UTF8.GetString(bytes));
    }

    private static bool IsBase64(string s)
        => s.Length > 0 && s.Length % 4 == 0 && Regex.IsMatch(s, "^[A-Za-z0-9+/]*={0,2}$");

    private static string LongestBase64Run(string s)
    {
        string best = "";
        foreach (Match m in Regex.Matches(s, "[A-Za-z0-9+/]{40,}={0,2}"))
            if (m.Value.Length > best.Length) best = m.Value;
        // 截到 4 的整数倍，避免尾部残缺导致 padding 报错
        int len = best.Length - best.Length % 4;
        return best[..len];
    }
}

public sealed record EnrollRequest
{
    [JsonPropertyName("did")] public string Did { get; init; } = "";
    [JsonPropertyName("secret")] public string Secret { get; init; } = "";
}

public sealed record EnrollResponse
{
    [JsonPropertyName("hub")] public string Hub { get; init; } = "";
    [JsonPropertyName("kid")] public string Kid { get; init; } = "";
    [JsonPropertyName("did")] public string Did { get; init; } = "";
    [JsonPropertyName("msg_key")] public string MessageKey { get; init; } = "";
    [JsonPropertyName("cert_thumbprint")] public string CertThumbprint { get; init; } = "";
}
