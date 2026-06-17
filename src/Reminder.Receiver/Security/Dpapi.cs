using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Reminder.Receiver.Security;

/// <summary>
/// 通过 P/Invoke 调用 Windows DPAPI（crypt32.dll）做当前用户范围的静态加密，
/// 用于保护主密钥/设备 token 落盘。离线环境无法用 ProtectedData NuGet 包，故直接 P/Invoke。
/// </summary>
[SupportedOSPlatform("windows")]
public static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr,
        IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    public static string Protect(byte[] data)
    {
        var inBlob = ToBlob(data);
        var outBlob = new DATA_BLOB();
        try
        {
            if (!CryptProtectData(ref inBlob, "ReminderHub", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                throw new InvalidOperationException("CryptProtectData 失败: " + Marshal.GetLastWin32Error());
            return Convert.ToBase64String(FromBlob(outBlob));
        }
        finally
        {
            FreeIn(inBlob);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    public static byte[] Unprotect(string base64)
    {
        var inBlob = ToBlob(Convert.FromBase64String(base64));
        var outBlob = new DATA_BLOB();
        try
        {
            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                throw new InvalidOperationException("CryptUnprotectData 失败: " + Marshal.GetLastWin32Error());
            return FromBlob(outBlob);
        }
        finally
        {
            FreeIn(inBlob);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    private static DATA_BLOB ToBlob(byte[] data)
    {
        var blob = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
        Marshal.Copy(data, 0, blob.pbData, data.Length);
        return blob;
    }

    private static byte[] FromBlob(DATA_BLOB blob)
    {
        var data = new byte[blob.cbData];
        Marshal.Copy(blob.pbData, data, 0, blob.cbData);
        return data;
    }

    private static void FreeIn(DATA_BLOB blob)
    {
        if (blob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(blob.pbData);
    }
}
