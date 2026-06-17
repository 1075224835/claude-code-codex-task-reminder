using System.Security.Cryptography;
using System.Text;
using Reminder.Protocol.Dtos;

namespace Reminder.Protocol.Crypto;

/// <summary>
/// AES-256-GCM 信封加解密 —— 系统安全核心。.NET 与安卓两端实现必须字节级一致。
///
/// AAD（被认证、不加密）= UTF8("{kid}|{did}|{ts}|v1")，绑定信封头部，防篡改。
/// 明文 = UTF8(JSON(payload))。
/// </summary>
public static class EnvelopeCrypto
{
    /// <summary>把任意可序列化负载加密为信封（随机 nonce）。</summary>
    public static EncryptedEnvelope Encrypt<T>(T payload, string kid, string did, byte[] msgKey, long ts)
        => Encrypt(payload, kid, did, msgKey, ts, RandomNumberGenerator.GetBytes(ProtocolConstants.GcmNonceBytes));

    /// <summary>显式 nonce 版本（用于生成确定性测试向量）。</summary>
    public static EncryptedEnvelope Encrypt<T>(T payload, string kid, string did, byte[] msgKey, long ts, byte[] nonce)
    {
        byte[] plaintext = ProtocolJson.ToUtf8(payload);
        byte[] aad = BuildAad(kid, did, ts);
        byte[] ct = new byte[plaintext.Length];
        byte[] tag = new byte[ProtocolConstants.GcmTagBytes];

        using var gcm = new AesGcm(msgKey, ProtocolConstants.GcmTagBytes);
        gcm.Encrypt(nonce, plaintext, ct, tag, aad);

        return new EncryptedEnvelope
        {
            Kid = kid,
            Did = did,
            Ts = ts,
            Nonce = Convert.ToBase64String(nonce),
            Ct = Convert.ToBase64String(ct),
            Tag = Convert.ToBase64String(tag),
        };
    }

    /// <summary>
    /// 解密并验证信封，反序列化为 T。
    /// 任何头部篡改、密钥不符或密文损坏都会抛 <see cref="CryptographicException"/>。
    /// </summary>
    public static T Decrypt<T>(EncryptedEnvelope env, byte[] msgKey)
    {
        byte[] nonce = DecodeCanonicalBase64(env.Nonce, ProtocolConstants.GcmNonceBytes, "nonce");
        byte[] ct = DecodeCanonicalBase64(env.Ct, null, "ct");
        byte[] tag = DecodeCanonicalBase64(env.Tag, ProtocolConstants.GcmTagBytes, "tag");
        byte[] aad = BuildAad(env.Kid, env.Did, env.Ts);
        byte[] plaintext = new byte[ct.Length];

        using var gcm = new AesGcm(msgKey, ProtocolConstants.GcmTagBytes);
        gcm.Decrypt(nonce, ct, tag, plaintext, aad); // tag 不符即抛异常

        return ProtocolJson.FromUtf8<T>(plaintext);
    }

    internal static byte[] BuildAad(string kid, string did, long ts)
        => Encoding.UTF8.GetBytes($"{kid}|{did}|{ts}|v1");

    public static byte[] DecodeCanonicalBase64(string value, int? expectedLength, string fieldName)
    {
        byte[] data;
        try { data = Convert.FromBase64String(value); }
        catch (Exception e) when (e is FormatException or ArgumentNullException)
        {
            throw new FormatException($"{fieldName} 不是合法的 base64。", e);
        }

        if (Convert.ToBase64String(data) != value)
            throw new FormatException($"{fieldName} 不是规范 base64。");
        if (expectedLength is int len && data.Length != len)
            throw new FormatException($"{fieldName} 长度必须为 {len} 字节。");
        return data;
    }
}
