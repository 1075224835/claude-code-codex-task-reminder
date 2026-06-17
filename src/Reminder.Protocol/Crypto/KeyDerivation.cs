using System.Security.Cryptography;
using System.Text;

namespace Reminder.Protocol.Crypto;

/// <summary>
/// 密钥生成与派生。算法须与安卓端逐一对齐：
///   K_msg = HKDF-SHA256(ikm = masterKey, salt = deviceToken, info = "reminder-v1|"+did, L = 32)
/// </summary>
public static class KeyDerivation
{
    /// <summary>新建 256 位工作区主密钥。</summary>
    public static byte[] NewMasterKey() => RandomNumberGenerator.GetBytes(ProtocolConstants.KeyBytes);

    /// <summary>新建每设备 token（作为 HKDF salt）。</summary>
    public static byte[] NewDeviceToken() => RandomNumberGenerator.GetBytes(ProtocolConstants.DeviceTokenBytes);

    /// <summary>派生某设备的消息密钥。</summary>
    public static byte[] DeriveMessageKey(byte[] masterKey, byte[] deviceToken, string deviceId)
    {
        byte[] info = Encoding.UTF8.GetBytes(ProtocolConstants.HkdfInfoPrefix + deviceId);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, ProtocolConstants.KeyBytes, deviceToken, info);
    }
}
