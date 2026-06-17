using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Reminder.Sender.Config;
using Reminder.Sender.Sending;
using Reminder.Sender.Tray;
using Reminder.Sender.Watchers;

namespace Reminder.Sender;

/// <summary>发送端常驻托盘程序：托盘图标 + Codex 监视器 + 友好配对对话框。</summary>
public sealed class TrayApp
{
    private AgentTray? _tray;
    private CancellationTokenSource? _cts;
    private bool _paused;
    private bool _enrolled;
    private bool _connected = true;
    private System.Windows.Forms.Timer? _heartbeat;

    public void Run()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        _tray = new AgentTray { StatusProvider = StatusText };
        _tray.ConnectRequested += OnConnect;
        _tray.InstallClaudeHooksRequested += OnInstallClaudeHooks;
        _tray.SendTypeRequested += OnSendType;
        _tray.TypeFilterToggled += OnTypeFilterToggled;
        _tray.TypeEnabledProvider = t => { try { return AgentCore.IsTypeEnabled(t); } catch { return true; } };
        _tray.PauseToggled += OnPause;
        _tray.ShowLogRequested += OnShowLog;
        _tray.DefenderExclusionRequested += OnAddDefenderExclusion;
        _tray.ExitRequested += OnExit;

        _enrolled = AgentCore.IsEnrolled;
        if (_enrolled) { StartWatcher(); StartHeartbeat(); }
        else _tray.Notify("全屏提醒 发送端", "尚未连接接收端。右键托盘图标 → 连接到接收端。");

        Application.Run(); // 消息循环（托盘常驻）
    }

    private string StatusText()
    {
        if (!_enrolled) return "未连接（请先连接到接收端）";
        string hub = AgentCore.HubAddress() ?? "";
        string conn = _connected ? "" : "  ·  ⚠ 接收端未连接";
        return (_paused ? "已暂停" : "运行中") + (hub.Length > 0 ? $"  ·  {hub}" : "") + conn;
    }

    private void StartHeartbeat()
    {
        if (_heartbeat != null) return;
        _heartbeat = new System.Windows.Forms.Timer { Interval = 20000 }; // 每 20s 探测接收端是否可达
        _heartbeat.Tick += OnHeartbeat;
        _heartbeat.Start();
    }

    // 心跳：探测接收端连通性；断线时托盘告警，恢复时补发暂存提醒。async void 在 UI 线程恢复，可安全更新托盘。
    private async void OnHeartbeat(object? sender, EventArgs e)
    {
        if (!_enrolled) return;
        bool ok = await AgentCore.PingAsync();
        if (ok && !_connected)
        {
            _connected = true;
            _tray!.SetConnectionState(true);
            int n = await OutboxQueue.DrainAsync();
            _tray!.Notify("全屏提醒 发送端", n > 0 ? $"已恢复连接，补发 {n} 条暂存提醒 ✓" : "已恢复与接收端的连接 ✓");
        }
        else if (!ok && _connected)
        {
            _connected = false;
            _tray!.SetConnectionState(false);
            _tray!.Notify("全屏提醒 发送端", "⚠ 接收端连不上了。期间的提醒会暂存，恢复后自动补发。");
        }
        else if (ok)
        {
            await OutboxQueue.DrainAsync(); // 已连接：顺手补发可能积压的（钩子进程抖动时入队的）
        }
    }

    private void StartWatcher()
    {
        StopWatcher();
        _cts = new CancellationTokenSource();
        ReminderSender sender;
        try { sender = new ReminderSender(); }
        catch { return; }
        var ct = _cts.Token;
        _ = Task.Run(() => new CodexRolloutWatcher(sender).RunAsync(ct), ct);
    }

    private void StopWatcher()
    {
        try { _cts?.Cancel(); } catch { /* 忽略 */ }
        _cts = null;
    }

    private void OnConnect()
    {
        using var dlg = new ConnectDialog();
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            AgentCore.Enroll(dlg.Code);
            _enrolled = true;
            _connected = true;
            _tray!.SetConnectionState(true);
            _tray!.Notify("全屏提醒 发送端", "已连接到接收端 ✓");
            if (!_paused) StartWatcher();
            StartHeartbeat();
        }
        catch (Exception ex)
        {
            MessageBox.Show("连接失败：" + ex.Message, "全屏提醒", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // async void：在 UI 线程 await（不阻塞消息循环），避免 .GetResult() 与 SyncContext 死锁。
    private async void OnSendType(string type)
    {
        if (!_enrolled) { MessageBox.Show("请先「连接到接收端」。", "全屏提醒"); return; }
        try
        {
            string name = Reminder.Protocol.Types.ReminderTypes.DefaultDisplayName(type);
            var m = AgentCore.NewReminder(type, "手动发送", "手动测试",
                $"这是一条手动发送的「{name}」提醒。", "manual-session", "manual", Environment.CurrentDirectory);
            var (ok, status) = await AgentCore.SendAsync(m);
            _tray!.Notify("全屏提醒 发送端", ok ? $"已发送（{name}）✓" : "发送失败：" + status);
        }
        catch (Exception ex)
        {
            _tray!.Notify("全屏提醒 发送端", "发送失败：" + ex.Message);
        }
    }

    private void OnTypeFilterToggled(string type, bool enabled)
    {
        if (!_enrolled) { _tray!.Notify("全屏提醒 发送端", "请先「连接到接收端」再设置发送类型。"); return; }
        try
        {
            AgentCore.SetTypeEnabled(type, enabled);
            string name = Reminder.Protocol.Types.ReminderTypes.DefaultDisplayName(type);
            _tray!.Notify("全屏提醒 发送端", $"「{name}」自动发送已{(enabled ? "开启" : "关闭")}");
        }
        catch (Exception ex) { _tray!.Notify("全屏提醒 发送端", "保存失败：" + ex.Message); }
    }

    private void OnPause(bool paused)
    {
        _paused = paused;
        if (paused) StopWatcher();
        else if (_enrolled) StartWatcher();
        _tray!.Notify("全屏提醒 发送端", paused ? "已暂停监视" : "已恢复监视");
    }

    private void OnShowLog()
    {
        try
        {
            Directory.CreateDirectory(SenderPaths.LogDir);
            var files = Directory.GetFiles(SenderPaths.LogDir, "watch-*.log");
            string target = files.Length > 0 ? files[^1] : SenderPaths.LogDir;
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch { /* 忽略 */ }
    }

    private void OnInstallClaudeHooks()
    {
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "reminder-agent.exe");
        var (ok, _, msg) = ClaudeHookInstaller.Install(exe);
        MessageBox.Show(msg, "全屏提醒 发送端 — 接入 Claude Code",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void OnAddDefenderExclusion()
    {
        string codex = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
        var confirm = MessageBox.Show(
            "这会把 Codex 会话目录加入 Windows Defender 排除项。\r\n\r\n" +
            "该目录位于当前用户可写区域，加入排除项会降低安全扫描覆盖。仅在会话文件扫描导致明显卡顿时使用。",
            "确认添加 Defender 排除项",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK) return;

        try
        {
            string pathArg = Convert.ToBase64String(Encoding.Unicode.GetBytes(codex));
            string script =
                "$p=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" + pathArg + "'));" +
                "Add-MpPreference -ExclusionPath $p";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            var psi = new ProcessStartInfo(ps,
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}")
            {
                UseShellExecute = true,
                Verb = "runas", // 触发 UAC 提权（Add-MpPreference 需管理员）
            };
            Process.Start(psi);
            _tray!.Notify("全屏提醒 发送端", "已请求把 ~/.codex/sessions 加入 Defender 排除项（请在弹出的管理员提示中确认）。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "自动添加未成功：" + ex.Message + "\r\n\r\n可手动以「管理员」运行 PowerShell 执行：\r\n" +
                $"Add-MpPreference -ExclusionPath \"{codex}\"",
                "全屏提醒 发送端", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OnExit()
    {
        StopWatcher();
        try { _heartbeat?.Stop(); _heartbeat?.Dispose(); } catch { /* 忽略 */ }
        _tray?.Dispose();
        Application.Exit();
    }
}
