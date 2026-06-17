using System.Media;
using System.IO;
using System.Windows.Interop;
using System.Windows.Threading;
using Reminder.Protocol.Dtos;
using Reminder.Receiver.Prefs;

namespace Reminder.Receiver.Overlay;

/// <summary>
/// 覆盖窗管理：收到提醒在 UI 线程为每个目标显示器各弹一个全屏覆盖窗，按 id 跟踪以便统一关闭。
/// </summary>
public sealed class OverlayManager
{
    private readonly Dispatcher _dispatcher;
    private readonly PrefsStore _prefs;
    private readonly MonitorStore _monitors;
    private readonly Dictionary<string, List<AlertWindow>> _open = new();

    public OverlayManager(Dispatcher dispatcher, PrefsStore prefs, MonitorStore monitors)
    {
        _dispatcher = dispatcher;
        _prefs = prefs;
        _monitors = monitors;
    }

    public void Show(ReminderMessage m)
    {
        _dispatcher.Invoke(() =>
        {
            var pref = _prefs.Prefs.ForType(m.Type);
            int autoClose = _prefs.Prefs.ResolveAutoClose(m.Type);

            var targets = ScreenManager.ResolveTargets(_monitors.Prefs);
            if (targets.Count == 0) targets.Add(ScreenManager.Primary());

            if (!_open.TryGetValue(m.Id, out var list))
            {
                list = new List<AlertWindow>();
                _open[m.Id] = list;
            }

            bool first = true;
            foreach (var mon in targets)
            {
                var w = new AlertWindow(m, pref, _prefs.Prefs.Background, autoClose);
                w.DismissRequested += () => CloseId(m.Id);
                list.Add(w);
                w.Show();
                var h = new WindowInteropHelper(w).Handle;
                NativeMethods.CoverMonitor(h, mon.Left, mon.Top, mon.Width, mon.Height);
                NativeMethods.ForceForeground(h);
                if (first) { PlaySound(pref); first = false; }
            }
        });
    }

    public void CloseId(string id)
    {
        _dispatcher.Invoke(() =>
        {
            if (!_open.TryGetValue(id, out var list)) return;
            foreach (var w in list.ToList())
            {
                try { w.Close(); } catch { /* 忽略 */ }
            }
            _open.Remove(id);
        });
    }

    private static void PlaySound(TypePref pref)
    {
        try
        {
            if (!string.IsNullOrEmpty(pref.Sound) && File.Exists(pref.Sound))
                new SoundPlayer(pref.Sound).Play();
            else
                SystemSounds.Exclamation.Play();
        }
        catch { /* 忽略 */ }
    }
}
