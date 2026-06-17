using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Reminder.Receiver.Security;

/// <summary>
/// 生成/加载 Hub 的自签 TLS 服务器证书。发送端按指纹 pinning 信任它，
/// 因此 SAN/有效期不参与校验（指纹匹配即接受）。
/// </summary>
public static class SelfSignedCert
{
    public static byte[] GeneratePfx()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=ReminderHub", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName(Environment.MachineName);
        san.AddIpAddress(IPAddress.Loopback);
        foreach (var ip in LocalIPv4s()) san.AddIpAddress(ip);
        req.CertificateExtensions.Add(san.Build());

        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // serverAuth

        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return cert.Export(X509ContentType.Pfx);
    }

    /// <summary>从 PFX 加载可被 Kestrel/Schannel 使用的服务器证书（私钥持久化）。</summary>
    public static X509Certificate2 LoadServerCert(byte[] pfx)
        => new(pfx, (string?)null,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);

    /// <summary>证书的 SHA-256 指纹（对 DER RawData 取 SHA-256，大写 hex）。用于 pinning，取代弱的 SHA-1 Thumbprint。</summary>
    public static string Sha256Thumbprint(X509Certificate2 cert)
        => Convert.ToHexString(SHA256.HashData(cert.RawData));

    private static IEnumerable<IPAddress> LocalIPv4s()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !ua.Address.Equals(IPAddress.Loopback))
                    yield return ua.Address;
        }
    }
}
