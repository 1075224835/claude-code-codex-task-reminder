namespace Reminder.Sender.Security;

/// <summary>
/// 跨平台密钥保护：Windows 用 DPAPI（dpapi: 前缀），其它平台退化为 base64（plain: 前缀，
/// 依赖文件权限 0600）。落盘字符串自带前缀以便正确反向解析。
/// </summary>
internal static class Secret
{
    public static string Protect(byte[] data)
        => OperatingSystem.IsWindows()
            ? "dpapi:" + Dpapi.Protect(data)
            : "plain:" + Convert.ToBase64String(data);

    public static byte[] Unprotect(string stored)
    {
        if (stored.StartsWith("dpapi:", StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("DPAPI 密文只能在 Windows 上解开。");
            return Dpapi.Unprotect(stored["dpapi:".Length..]);
        }
        if (stored.StartsWith("plain:", StringComparison.Ordinal))
            return Convert.FromBase64String(stored["plain:".Length..]);
        return Convert.FromBase64String(stored); // 兼容无前缀
    }
}
