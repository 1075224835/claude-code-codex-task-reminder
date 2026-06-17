namespace Reminder.Protocol;

/// <summary>
/// 协议级常量。修改任何一项都会破坏与安卓端的兼容性，须同步更新 docs/PROTOCOL.md。
/// </summary>
public static class ProtocolConstants
{
    /// <summary>线协议版本。</summary>
    public const int Version = 1;

    /// <summary>HKDF 派生消息密钥时使用的 info 前缀（实际 info = Prefix + did）。</summary>
    public const string HkdfInfoPrefix = "reminder-v1|";

    /// <summary>AES-GCM 认证标签字节数（128 位，与安卓 AES/GCM/NoPadding 默认一致）。</summary>
    public const int GcmTagBytes = 16;

    /// <summary>AES-GCM Nonce/IV 字节数（96 位，GCM 推荐值）。</summary>
    public const int GcmNonceBytes = 12;

    /// <summary>派生消息密钥长度（256 位）。</summary>
    public const int KeyBytes = 32;

    /// <summary>设备 token 字节数。</summary>
    public const int DeviceTokenBytes = 32;

    /// <summary>时间戳新鲜度窗口（秒）。超出 ±此值的消息按重放/过期拒收。</summary>
    public const int FreshnessWindowSeconds = 120;
}
