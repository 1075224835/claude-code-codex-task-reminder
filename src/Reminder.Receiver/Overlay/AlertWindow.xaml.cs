using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Reminder.Protocol.Dtos;
using Reminder.Receiver.Prefs;

namespace Reminder.Receiver.Overlay;

/// <summary>单屏全屏覆盖窗。按类型偏好渲染背景/文字/强调色，支持图片背景与倒计时自动关闭。</summary>
public partial class AlertWindow : Window
{
    /// <summary>用户请求关闭（按钮/Esc）或倒计时归零。由 OverlayManager 统一关闭同一 id 的所有窗。</summary>
    public event Action? DismissRequested;

    public string ReminderId { get; }

    private readonly int _autoCloseSeconds;
    private DispatcherTimer? _timer;
    private int _remaining;

    public AlertWindow(ReminderMessage m, TypePref pref, string background, int autoCloseSeconds)
    {
        InitializeComponent();
        ReminderId = m.Id;
        _autoCloseSeconds = autoCloseSeconds;

        bool usingImage = ApplyBackground(background);
        Scrim.Visibility = usingImage ? Visibility.Visible : Visibility.Collapsed;

        var accent = ParseColor(pref.Color);
        AccentBar.Background = new SolidColorBrush(accent);
        TypeBadge.Background = new SolidColorBrush(accent);
        TypeText.Text = string.IsNullOrEmpty(pref.DisplayName) ? m.Type : pref.DisplayName;

        BodyText.Text = m.Render(string.IsNullOrEmpty(pref.Template) ? "{host} · {project}\n{detail}" : pref.Template);
        MetaText.Text = BuildMeta(m);
        SetSource(m.Agent);

        Loaded += (_, _) => StartCountdown();
    }

    private void StartCountdown()
    {
        if (_autoCloseSeconds <= 0) { CountdownText.Visibility = Visibility.Collapsed; return; }
        _remaining = _autoCloseSeconds;
        CountdownText.Visibility = Visibility.Visible;
        UpdateCountdownText();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _remaining--;
            if (_remaining <= 0)
            {
                StopTimer();
                DismissRequested?.Invoke();
            }
            else UpdateCountdownText();
        };
        _timer.Start();
    }

    private void UpdateCountdownText() => CountdownText.Text = $"{_remaining} 秒后自动关闭";

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>按消息来源(agent)显示来源主题色徽章：Claude(橙) / Codex(绿)；其它来源(手动等)隐藏。</summary>
    private void SetSource(string agent)
    {
        string a = (agent ?? "").ToLowerInvariant();
        if (a.Contains("claude")) ShowSource("Claude Code", "#D97757");   // Anthropic 品牌橙
        else if (a.Contains("codex")) ShowSource("Codex", "#10A37F");     // OpenAI 品牌绿
        else SourceBadge.Visibility = Visibility.Collapsed;
    }

    private void ShowSource(string name, string colorHex)
    {
        SourceName.Text = name;
        SourceBadge.Background = new SolidColorBrush(ParseColor(colorHex));
        SourceBadge.Visibility = Visibility.Visible;
    }

    private static string BuildMeta(ReminderMessage m)
    {
        string sess = m.SessionId.Length > 8 ? m.SessionId[..8] : m.SessionId;
        string time = m.CreatedAt;
        if (DateTimeOffset.TryParse(m.CreatedAt, out var dto)) time = dto.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(m.Host)) parts.Add($"主机 {m.Host}");
        if (!string.IsNullOrEmpty(m.Project)) parts.Add($"项目 {m.Project}");
        if (!string.IsNullOrEmpty(m.Agent)) parts.Add(m.Agent);
        if (!string.IsNullOrEmpty(sess)) parts.Add($"会话 {sess}");
        parts.Add(time);
        return string.Join("   ·   ", parts);
    }

    /// <returns>是否使用了图片背景（用于决定是否显示蒙版）。</returns>
    private bool ApplyBackground(string bg)
    {
        try
        {
            if (bg.StartsWith("#"))
            {
                Background = new SolidColorBrush(ParseColor(bg));
                return false;
            }
            if (File.Exists(bg))
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri(bg);
                img.EndInit();
                Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
                return true;
            }
        }
        catch { /* 保留默认背景 */ }
        return false;
    }

    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.SlateGray; }
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        StopTimer();
        DismissRequested?.Invoke();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            StopTimer();
            DismissRequested?.Invoke();
        }
    }
}
