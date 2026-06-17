using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Reminder.Protocol.Crypto;
using Reminder.Protocol.Dtos;
using Reminder.Protocol.Types;
using Reminder.Receiver.Config;
using Reminder.Receiver.Hub;
using Reminder.Receiver.Logging;
using Reminder.Receiver.Net;
using Reminder.Receiver.Overlay;
using Reminder.Receiver.Prefs;
using Reminder.Receiver.Push;
using Reminder.Receiver.Security;
using Reminder.Receiver.Stats;
using Reminder.Receiver.Tray;
using Reminder.Receiver.Views;

namespace Reminder.Receiver;

public partial class App : Application
{
    private readonly ConfigManager _config = new();
    private readonly DeviceRegistry _registry = new();
    private readonly StatsStore _stats = new();
    private readonly PrefsStore _prefs = new();
    private readonly MonitorStore _monitors = new();
    private readonly ReminderBus _bus = new();
    private OverlayManager? _overlay;
    private HubServer? _hub;
    private TrayIcon? _tray;
    private SendersWindow? _sendersWindow;

    /// <summary>配对码有效期（秒）：发送端须在此时限内完成首次连接，逾期作废。</summary>
    private const int PairingTtlSeconds = 600;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 在创建任何窗口前设置进程级 Per-Monitor-V2 DPI 感知；WPF 窗口随后遵循该级别，
        // 保证混合 DPI 多屏下覆盖窗清晰、定位准确。
        try { System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2); }
        catch { /* 旧系统忽略 */ }

        AppPaths.EnsureBase();
        Log.Init(AppPaths.LogDir);

        try
        {
            _config.Load();
            _registry.Load();
            _stats.Load();
            _prefs.Load();
            _monitors.Load();
        }
        catch (Exception ex)
        {
            Log.Error("初始化失败: " + ex);
            MessageBox.Show("初始化失败：\n" + ex.Message, "全屏提醒", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _overlay = new OverlayManager(Dispatcher, _prefs, _monitors);
        _bus.ReminderReceived += m => _overlay!.Show(m);
        _bus.AckReceived += id => _overlay!.CloseId(id);

        foreach (var mon in ScreenManager.All())
            Log.Info($"检测到显示器 {mon.DeviceName}{(mon.Primary ? "(主)" : "")} {mon.Width}x{mon.Height} @({mon.Left},{mon.Top})");

        // --provision <file>：无界面生成一个发送端配对码并写入文件后退出（供脚本/验证使用）。
        if (e.Args.Contains("--provision"))
        {
            RunProvision(e.Args);
            return;
        }

        // --set-serverchan <sendkey>：无界面写入 Server酱 SendKey（DPAPI 保护）后退出。
        if (e.Args.Contains("--set-serverchan"))
        {
            RunSetServerChan(e.Args);
            return;
        }

        // --selftest：弹一条样例提醒，关闭即退出（无需网络的可视化验证）。
        if (e.Args.Contains("--selftest"))
        {
            RunSelfTest();
            return;
        }

        // 远程推送：Server酱（推微信）。未配则仅本机/LAN 全屏提醒。
        var forwarder = TryCreateServerChanForwarder();
        var router = new ReminderRouter(_config, _registry, new ReplayGuard(), _stats, _bus, _prefs, forwarder);
        _hub = new HubServer();
        _ = _hub.StartAsync(_config.Config, _config.MasterKey, _config.ServerCert, router, _stats, _registry)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Log.Error("Hub 启动失败: " + t.Exception);
            });

        _tray = new TrayIcon();
        _tray.AddSenderRequested += OnAddSender;
        _tray.ManageSendersRequested += OnManageSenders;
        _tray.SetServerChanRequested += OnSetServerChan;
        _tray.SetAddressRequested += OnSetAddress;
        _tray.TestRequested += () => _bus.PublishReminder(SampleReminder());
        _tray.StatsRequested += ShowStats;
        _tray.SetBackgroundRequested += OnSetBackground;
        _tray.ClearBackgroundRequested += OnClearBackground;
        _tray.SetCountdownRequested += OnSetCountdown;
        _tray.ScreenMenuProvider = BuildScreenMenu;
        _tray.OpenDataDirRequested += () => OpenDir(AppPaths.Base);
        _tray.ExitRequested += () => Shutdown();

        _tray.Notify("全屏提醒 接收端", $"Hub 运行中：{LanInfo.GuessHubUrl(_config.Config)}");
        Log.Info("接收端启动完成。");

        // --exit-test：启动后约 2.5 秒自动触发退出，用于验证 OnExit 不再卡死。
        if (e.Args.Contains("--exit-test"))
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            t.Tick += (_, _) => { t.Stop(); Shutdown(); };
            t.Start();
        }
    }

    private void OnAddSender()
    {
        long exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + PairingTtlSeconds;
        var nd = _registry.Add(NextSenderLabel(), "sender", exp);
        var blob = new ProvisioningBlob
        {
            Hub = LanInfo.GuessHubUrl(_config.Config),
            Kid = _config.Config.Kid,
            Did = nd.Device.Did,
            EnrollSecret = Convert.ToBase64String(nd.EnrollSecret),
            CertThumbprint = _config.Config.CertThumbprint,
            Kind = "sender",
            Exp = exp,
        };
        string code = blob.Encode();
        string content =
            $"Hub 地址: {blob.Hub}\r\n设备 ID: {blob.Did}\r\n\r\n配对码（含一次性登记密钥，{PairingTtlSeconds / 60} 分钟内有效，勿外传）：\r\n\r\n{code}\r\n\r\n" +
            "在发送端机器上：右键托盘「全屏提醒 发送端」图标 → 连接到接收端 →\r\n" +
            "复制本窗口内容或配对码 → 粘贴到连接对话框 → 点「连接」即可（需在有效期内）。";
        new TextDisplayWindow("添加发送端 — 配对码", content).Show();
        _sendersWindow?.RefreshRows();
        Log.Info($"已生成发送端配对码：{blob.Did}");
    }

    private string NextSenderLabel()
    {
        int count = _registry.All.Count(d =>
            string.Equals(d.Kind, "sender", StringComparison.OrdinalIgnoreCase) && !d.Revoked);
        return $"发送端 {count + 1}";
    }

    private void OnManageSenders()
    {
        if (_sendersWindow is { IsVisible: true })
        {
            _sendersWindow.RefreshRows();
            _sendersWindow.Activate();
            return;
        }

        var window = new SendersWindow(_registry);
        _sendersWindow = window;
        window.AddSenderRequested += OnAddSender;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_sendersWindow, window))
                _sendersWindow = null;
        };
        window.Show();
    }

    private IRemoteForwarder? TryCreateServerChanForwarder()
    {
        var c = _config.Config;
        if (string.IsNullOrWhiteSpace(c.ServerChanKeyProtected)) return null;
        try
        {
            string key = Encoding.UTF8.GetString(Dpapi.Unprotect(c.ServerChanKeyProtected!));
            Log.Info("Server酱 远程推送已启用（推送到微信）");
            return new ServerChanForwarder(key);
        }
        catch (Exception e) { Log.Error("Server酱 初始化失败: " + e.Message); return null; }
    }

    private void OnSetServerChan()
    {
        string current = "";
        var dlg = new InputWindow("设置 Server酱 推送",
            "粘贴你的 Server酱 SendKey（在 sct.ftqq.com 微信扫码登录后获取，SCT 开头）：", current);
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
        try
        {
            _config.Config.ServerChanKeyProtected = Dpapi.Protect(Encoding.UTF8.GetBytes(dlg.Value.Trim()));
            _config.Save();
            MessageBox.Show("Server酱 SendKey 已保存。重启接收端后推送到微信生效。", "全屏提醒", MessageBoxButton.OK, MessageBoxImage.Information);
            Log.Info("Server酱 SendKey 已设置");
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存失败：" + ex.Message, "全屏提醒", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSetAddress()
    {
        string current = string.IsNullOrWhiteSpace(_config.Config.AdvertiseHost)
            ? LanInfo.GuessLanIPv4()
            : _config.Config.AdvertiseHost!;
        var dlg = new InputWindow("设置接收端地址",
            "发送端将连接到这个地址（IP 或主机名）。自动探测选错网卡（如选到 VPN/虚拟网卡）时在此手动指定；留空=自动探测。",
            current);
        if (dlg.ShowDialog() == true)
        {
            string v = dlg.Value;
            _config.Config.AdvertiseHost = string.IsNullOrWhiteSpace(v) ? null : v;
            _config.Save();
            _tray?.Notify("全屏提醒", $"接收端地址：{LanInfo.GuessHubUrl(_config.Config)}\n请重新「添加发送端」生成新配对码。");
            Log.Info($"接收端地址改为 {LanInfo.GuessHubUrl(_config.Config)}");
        }
    }

    private void OnSetBackground()
    {
        var dlg = new System.Windows.Forms.OpenFileDialog
        {
            Title = "选择全屏提醒背景图片",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _prefs.Prefs.Background = dlg.FileName;
            _prefs.Save();
            _tray?.Notify("全屏提醒", "背景图片已设置，下次提醒生效（可用\"测试提醒\"预览）。");
            Log.Info("背景图片已设置：" + dlg.FileName);
        }
    }

    private void OnSetCountdown()
    {
        int cur = _prefs.Prefs.AutoCloseSeconds;
        var dlg = new InputWindow("设置自动关闭倒计时",
            "全屏提醒自动关闭的秒数（0 = 不自动关闭，常驻到手动关闭）。\n注：「需要输入/需要授权」两类默认常驻、不受此值影响。",
            cur.ToString());
        if (dlg.ShowDialog() != true) return;
        if (!int.TryParse(dlg.Value.Trim(), out int secs) || secs < 0)
        {
            MessageBox.Show("请输入 0 或正整数秒数。", "全屏提醒", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _prefs.Prefs.AutoCloseSeconds = secs;
        _prefs.Save();
        _tray?.Notify("全屏提醒", secs == 0 ? "已设为不自动关闭（需手动关闭）" : $"自动关闭倒计时已设为 {secs} 秒（下次提醒生效）");
        Log.Info($"自动关闭倒计时改为 {secs} 秒");
    }

    private void OnClearBackground()
    {
        _prefs.Prefs.Background = "#0E1116";
        _prefs.Save();
        _tray?.Notify("全屏提醒", "已恢复纯色背景。");
    }

    private void ShowStats()
    {
        var snap = _stats.Snapshot();
        var sb = new StringBuilder();
        sb.AppendLine("== 类型统计（总计）==");
        if (snap.Totals.Count == 0) sb.AppendLine("(暂无数据)");
        foreach (var kv in snap.Totals)
            sb.AppendLine($"{ReminderTypes.DefaultDisplayName(kv.Key)} [{kv.Key}] : {kv.Value}");
        sb.AppendLine();
        sb.AppendLine("== 按设备 / 类型 ==");
        foreach (var r in snap.ByDevice)
            sb.AppendLine($"{r.Host} [{r.Did}]  {r.Type} : {r.Count}");
        new TextDisplayWindow("统计", sb.ToString(), copyButton: false).Show();
    }

    private void RunProvision(string[] argv)
    {
        try
        {
            string? outPath = null;
            for (int i = 0; i < argv.Length - 1; i++)
                if (argv[i] == "--provision") { outPath = argv[i + 1]; break; }

            long exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + PairingTtlSeconds;
            var nd = _registry.Add(NextSenderLabel(), "sender", exp);
            var blob = new ProvisioningBlob
            {
                Hub = LanInfo.GuessHubUrl(_config.Config),
                Kid = _config.Config.Kid,
                Did = nd.Device.Did,
                EnrollSecret = Convert.ToBase64String(nd.EnrollSecret),
                CertThumbprint = _config.Config.CertThumbprint,
                Kind = "sender",
                Exp = exp,
            };
            string code = blob.Encode();
            if (outPath is not null) File.WriteAllText(outPath, code);
            Log.Info($"已生成发送端配对码（headless）：{blob.Did}");
        }
        catch (Exception ex) { Log.Error("provision 失败: " + ex); }
        Shutdown();
    }

    private List<(string label, bool enabled, Action toggle)> BuildScreenMenu()
    {
        var list = new List<(string, bool, Action)>
        {
            ("全部屏幕", _monitors.Prefs.Enabled.Count == 0, () => { _monitors.Prefs.Enabled.Clear(); _monitors.Save(); }),
        };
        foreach (var mon in ScreenManager.All())
        {
            string name = mon.DeviceName.Replace(@"\\.\", "");
            string label = $"{name}{(mon.Primary ? " (主)" : "")}  {mon.Width}×{mon.Height}";
            bool enabled = _monitors.Prefs.Enabled.Contains(mon.DeviceName);
            string dev = mon.DeviceName;
            list.Add((label, enabled, () =>
            {
                if (_monitors.Prefs.Enabled.Contains(dev)) _monitors.Prefs.Enabled.Remove(dev);
                else _monitors.Prefs.Enabled.Add(dev);
                _monitors.Save();
            }));
        }
        return list;
    }

    private void RunSetServerChan(string[] argv)
    {
        try
        {
            int i = Array.IndexOf(argv, "--set-serverchan");
            string key = (i >= 0 && i + 1 < argv.Length) ? argv[i + 1] : "";
            if (key.Length == 0)
            {
                Log.Error("--set-serverchan 需要参数：<sendkey>");
            }
            else
            {
                _config.Config.ServerChanKeyProtected = Dpapi.Protect(Encoding.UTF8.GetBytes(key));
                _config.Save();
                Log.Info($"已写入 Server酱 SendKey（末4位 …{key[^4..]}）");
            }
        }
        catch (Exception ex) { Log.Error("--set-serverchan 失败: " + ex); }
        Shutdown();
    }

    private void RunSelfTest()
    {
        var m = SampleReminder();
        var pref = _prefs.Prefs.ForType(m.Type);
        int autoClose = _prefs.Prefs.ResolveAutoClose(m.Type);
        var targets = ScreenManager.ResolveTargets(_monitors.Prefs);
        if (targets.Count == 0) targets.Add(ScreenManager.Primary());

        var windows = new List<AlertWindow>();
        void CloseAll()
        {
            foreach (var w in windows.ToList()) { try { w.Close(); } catch { /* 忽略 */ } }
            Shutdown();
        }

        foreach (var mon in targets)
        {
            var w = new AlertWindow(m, pref, _prefs.Prefs.Background, autoClose);
            w.DismissRequested += CloseAll;
            windows.Add(w);
            w.Show();
            var h = new WindowInteropHelper(w).Handle;
            NativeMethods.CoverMonitor(h, mon.Left, mon.Top, mon.Width, mon.Height);
            NativeMethods.ForceForeground(h);
        }
    }

    private static ReminderMessage SampleReminder() => new()
    {
        Type = ReminderTypes.NeedsInput,
        Host = Environment.MachineName,
        Project = "示例项目",
        Cwd = Environment.CurrentDirectory,
        SessionId = "demo-session-0001",
        Agent = "claude_code",
        Title = "测试提醒",
        Detail = "这是一条测试提醒：需要你的确认。",
        CreatedAt = DateTimeOffset.Now.ToString("O"),
    };

    private static void OpenDir(string dir)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("打开目录失败: " + ex.Message); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 先撤掉托盘图标（即时反馈）。
        try { _tray?.Dispose(); } catch { /* 忽略 */ }

        // 在后台线程停止 Hub，避免在 WPF UI 线程上 sync-over-async 死锁导致退出卡死；最多等 2 秒。
        try
        {
            var hub = _hub;
            if (hub is not null)
                Task.Run(async () => { try { await hub.StopAsync(); } catch { /* 忽略 */ } }).Wait(2000);
        }
        catch { /* 忽略 */ }

        base.OnExit(e);
    }
}
