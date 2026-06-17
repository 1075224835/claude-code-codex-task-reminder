using System.Drawing;
using System.Windows.Forms;
using Reminder.Protocol.Types;

namespace Reminder.Sender.Tray;

/// <summary>发送端系统托盘图标 + 菜单。</summary>
public sealed class AgentTray : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly Dictionary<string, ToolStripMenuItem> _typeFilterItems = new();

    public event Action? ConnectRequested;
    public event Action? InstallClaudeHooksRequested;
    public event Action<string>? SendTypeRequested;        // 手动发送：参数为消息类型 key
    public event Action<string, bool>? TypeFilterToggled;  // 自动发送过滤：(类型 key, 是否启用)
    public event Action<bool>? PauseToggled; // true = 暂停
    public event Action? ShowLogRequested;
    public event Action? DefenderExclusionRequested;
    public event Action? ExitRequested;

    /// <summary>菜单展开时回填状态文本。</summary>
    public Func<string>? StatusProvider;
    /// <summary>查询某类型当前是否启用自动发送（用于回填勾选状态）。</summary>
    public Func<string, bool>? TypeEnabledProvider;

    public AgentTray()
    {
        _statusItem = new ToolStripMenuItem("状态：启动中…") { Enabled = false };
        _pauseItem = new ToolStripMenuItem("暂停监视") { CheckOnClick = true };
        _pauseItem.Click += (_, _) => PauseToggled?.Invoke(_pauseItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("连接到接收端…", null, (_, _) => ConnectRequested?.Invoke());
        menu.Items.Add("接入 Claude Code（配置钩子）", null, (_, _) => InstallClaudeHooksRequested?.Invoke());

        // 自动发送的类型：勾选=该类型在 Claude/Codex 触发时会发送；取消勾选=静默忽略该类型。
        var filterMenu = new ToolStripMenuItem("自动发送的类型（勾选=发送）");
        foreach (var key in ReminderTypes.Defaults)
        {
            string k = key;
            var item = new ToolStripMenuItem(ReminderTypes.DefaultDisplayName(key)) { CheckOnClick = true, Checked = true };
            item.Click += (_, _) => TypeFilterToggled?.Invoke(k, item.Checked);
            _typeFilterItems[k] = item;
            filterMenu.DropDownItems.Add(item);
        }
        filterMenu.DropDownOpening += (_, _) =>
        {
            if (TypeEnabledProvider is null) return;
            foreach (var kv in _typeFilterItems) kv.Value.Checked = TypeEnabledProvider(kv.Key);
        };
        menu.Items.Add(filterMenu);

        // 手动发送一条（无视上面的过滤，用于测试）
        var sendMenu = new ToolStripMenuItem("手动发送一条…");
        foreach (var key in ReminderTypes.Defaults)
        {
            string k = key;
            sendMenu.DropDownItems.Add(ReminderTypes.DefaultDisplayName(key), null, (_, _) => SendTypeRequested?.Invoke(k));
        }
        menu.Items.Add(sendMenu);
        menu.Items.Add(_pauseItem);
        menu.Items.Add("查看日志", null, (_, _) => ShowLogRequested?.Invoke());
        menu.Items.Add("添加 Defender 排除项（修复卡顿）", null, (_, _) => DefenderExclusionRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());
        menu.Opening += (_, _) => { if (StatusProvider != null) _statusItem.Text = "状态：" + StatusProvider(); };

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "全屏提醒 发送端",
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ConnectRequested?.Invoke();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var s = typeof(AgentTray).Assembly.GetManifestResourceStream("Reminder.Sender.app.ico");
            if (s is not null) return new Icon(s);
        }
        catch { /* 回退 */ }
        return SystemIcons.Application;
    }

    public void Notify(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(3000);
    }

    /// <summary>反映与接收端的连接状态（改 tooltip 文案；须在 UI 线程调用）。</summary>
    public void SetConnectionState(bool connected)
        => _icon.Text = connected ? "全屏提醒 发送端" : "全屏提醒 发送端（⚠ 接收端未连接）";

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
