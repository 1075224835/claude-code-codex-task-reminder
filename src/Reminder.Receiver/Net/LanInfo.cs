using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Reminder.Receiver.Config;

namespace Reminder.Receiver.Net;

/// <summary>探测本机 LAN IPv4，生成发送端可连接的 Hub 地址。可被配置里的 AdvertiseHost 覆盖。</summary>
public static class LanInfo
{
    public static string GuessHubUrl(ReceiverConfig cfg)
    {
        string host = !string.IsNullOrWhiteSpace(cfg.AdvertiseHost) ? cfg.AdvertiseHost!.Trim() : GuessLanIPv4();
        return $"{cfg.Scheme}://{host}:{cfg.HubPort}";
    }

    /// <summary>
    /// 选最像「真实物理 LAN」的 IPv4：优先有默认网关、非虚拟/非隧道、有线/无线网卡。
    /// 跳过虚拟网卡(Hyper-V/VMware/VirtualBox/WSL/Tailscale/WireGuard/VPN/TAP)、APIPA、隧道。
    /// </summary>
    public static string GuessLanIPv4()
    {
        string bestIp = "";
        int bestScore = int.MinValue;

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            var props = ni.GetIPProperties();
            bool hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

            string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
            bool isVirtual = desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("hyper-v")
                || desc.Contains("vethernet") || desc.Contains("virtualbox") || desc.Contains("tailscale")
                || desc.Contains("wsl") || desc.Contains("tap") || desc.Contains("tun-")
                || desc.Contains("zerotier") || desc.Contains("wireguard") || desc.Contains("vpn")
                || desc.Contains("loopback") || desc.Contains("pseudo");

            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                string ip = ua.Address.ToString();
                if (ip.StartsWith("169.254.")) continue;   // APIPA
                if (!IsPrivate(ua.Address)) continue;       // 只用 RFC1918 私网地址

                int score = 0;
                if (hasGateway) score += 100;               // 有默认网关 = 真实上网网卡
                if (!isVirtual) score += 50;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 10;
                else if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 8;
                if (ip.StartsWith("192.168.")) score += 5;  // 家用网段优先
                else if (ip.StartsWith("10.")) score += 3;

                if (score > bestScore) { bestScore = score; bestIp = ip; }
            }
        }

        return bestIp.Length > 0 ? bestIp : "127.0.0.1";
    }

    private static bool IsPrivate(IPAddress addr)
    {
        byte[] b = addr.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
    }
}
