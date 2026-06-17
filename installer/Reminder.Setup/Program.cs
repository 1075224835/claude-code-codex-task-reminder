using System.Windows.Forms;

namespace Reminder.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var a = args.Select(x => x.ToLowerInvariant()).ToArray();

        // 静默模式（脚本/测试）： /silent [/receiver-only|/sender-only]   或   /uninstall [/purge]
        if (a.Contains("/silent") || a.Contains("/install"))
        {
            try
            {
                bool recv = !a.Contains("/sender-only");
                bool sender = !a.Contains("/receiver-only");
                Installer.Install(recv, sender, a.Contains("/claude-hooks"));
                return 0;
            }
            catch { return 1; }
        }
        if (a.Contains("/uninstall"))
        {
            try { Installer.Uninstall(a.Contains("/purge")); return 0; } catch { return 1; }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
        return 0;
    }
}
