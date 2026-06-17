using System.Drawing;
using System.Windows.Forms;

namespace Reminder.Receiver.Tray;

/// <summary>系统托盘图标 + 右键菜单（WinForms NotifyIcon，运行在 WPF UI 线程）。</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public event Action? AddSenderRequested;
    public event Action? ManageSendersRequested;
    public event Action? SetServerChanRequested;
    public event Action? SetAddressRequested;
    public event Action? TestRequested;
    public event Action? StatsRequested;
    public event Action? SetBackgroundRequested;
    public event Action? ClearBackgroundRequested;
    public event Action? SetCountdownRequested;
    public event Action? OpenDataDirRequested;
    public event Action? ExitRequested;

    /// <summary>返回「提醒屏幕」子菜单项：(标签, 是否勾选, 点击切换)。打开时实时重建以反映当前接线。</summary>
    public Func<List<(string label, bool enabled, Action toggle)>>? ScreenMenuProvider;

    private readonly ToolStripMenuItem _screenMenu = new("提醒屏幕");

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("添加发送端…", null, (_, _) => AddSenderRequested?.Invoke());
        menu.Items.Add("管理发送端…", null, (_, _) => ManageSendersRequested?.Invoke());
        menu.Items.Add("设置 Server酱 推送(微信)…", null, (_, _) => SetServerChanRequested?.Invoke());
        menu.Items.Add("设置接收端地址…", null, (_, _) => SetAddressRequested?.Invoke());
        menu.Items.Add("测试提醒", null, (_, _) => TestRequested?.Invoke());
        menu.Items.Add("查看统计…", null, (_, _) => StatsRequested?.Invoke());
        menu.Items.Add(_screenMenu);
        _screenMenu.DropDownOpening += (_, _) => RebuildScreenMenu();
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("设置自动关闭倒计时…", null, (_, _) => SetCountdownRequested?.Invoke());
        menu.Items.Add("设置背景图片…", null, (_, _) => SetBackgroundRequested?.Invoke());
        menu.Items.Add("恢复纯色背景", null, (_, _) => ClearBackgroundRequested?.Invoke());
        menu.Items.Add("打开数据目录", null, (_, _) => OpenDataDirRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "全屏提醒 接收端",
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => StatsRequested?.Invoke();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var s = typeof(TrayIcon).Assembly.GetManifestResourceStream("Reminder.Receiver.app.ico");
            if (s is not null) return new Icon(s);
        }
        catch { /* 回退 */ }
        return SystemIcons.Information;
    }

    private void RebuildScreenMenu()
    {
        _screenMenu.DropDownItems.Clear();
        var entries = ScreenMenuProvider?.Invoke() ?? new List<(string, bool, Action)>();
        foreach (var (label, enabled, toggle) in entries)
        {
            var item = new ToolStripMenuItem(label) { Checked = enabled };
            item.Click += (_, _) => toggle();
            _screenMenu.DropDownItems.Add(item);
        }
        if (_screenMenu.DropDownItems.Count == 0)
            _screenMenu.DropDownItems.Add(new ToolStripMenuItem("(未检测到显示器)") { Enabled = false });
    }

    public void Notify(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
