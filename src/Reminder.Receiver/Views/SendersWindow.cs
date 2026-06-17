using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Reminder.Receiver.Hub;

namespace Reminder.Receiver.Views;

/// <summary>接收端发送端管理窗口：查看多台发送端状态，并可新增或撤销单台发送端。</summary>
public sealed class SendersWindow : Window
{
    private readonly DeviceRegistry _registry;
    private readonly TextBlock _summary;
    private readonly DataGrid _grid;
    private readonly Button _revoke;

    public event Action? AddSenderRequested;

    public SendersWindow(DeviceRegistry registry)
    {
        _registry = registry;

        Title = "管理发送端";
        Width = 860;
        Height = 480;
        MinWidth = 760;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _summary = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(_summary, 0);
        root.Children.Add(_summary);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new Binding(nameof(SenderRow.Label)), Width = new DataGridLength(150) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "设备编号", Binding = new Binding(nameof(SenderRow.Did)), Width = new DataGridLength(170) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new Binding(nameof(SenderRow.Status)), Width = new DataGridLength(90) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "最近主机", Binding = new Binding(nameof(SenderRow.LastHost)), Width = new DataGridLength(130) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "最近活跃", Binding = new Binding(nameof(SenderRow.LastSeenAt)), Width = new DataGridLength(150) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "配对截止", Binding = new Binding(nameof(SenderRow.EnrollBy)), Width = new DataGridLength(150) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "创建时间", Binding = new Binding(nameof(SenderRow.CreatedAt)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.SelectionChanged += (_, _) => UpdateButtonState();
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var add = new Button { Content = "新增发送端", MinWidth = 110, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) =>
        {
            AddSenderRequested?.Invoke();
            RefreshRows();
        };
        buttons.Children.Add(add);

        var refresh = new Button { Content = "刷新", MinWidth = 82, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        refresh.Click += (_, _) => RefreshRows();
        buttons.Children.Add(refresh);

        _revoke = new Button { Content = "撤销选中", MinWidth = 98, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        _revoke.Click += (_, _) => RevokeSelected();
        buttons.Children.Add(_revoke);

        var close = new Button { Content = "关闭", MinWidth = 82, Padding = new Thickness(14, 6, 14, 6), IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        RefreshRows();
    }

    public void RefreshRows()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var rows = _registry.All
            .Where(d => string.Equals(d.Kind, "sender", StringComparison.OrdinalIgnoreCase))
            .Select(d => SenderRow.From(d, now))
            .OrderBy(r => r.SortGroup)
            .ThenByDescending(r => r.CreatedTicks)
            .ToList();

        _grid.ItemsSource = rows;

        int active = rows.Count(r => r.Status == "已连接");
        int pending = rows.Count(r => r.Status == "待配对");
        int expired = rows.Count(r => r.Status == "已过期");
        int revoked = rows.Count(r => r.Status == "已撤销");
        _summary.Text = $"共 {rows.Count} 个发送端：已连接 {active}，待配对 {pending}，已过期 {expired}，已撤销 {revoked}。每台发送端请使用单独生成的配对码。";

        UpdateButtonState();
    }

    private void UpdateButtonState()
        => _revoke.IsEnabled = _grid.SelectedItem is SenderRow { CanRevoke: true };

    private void RevokeSelected()
    {
        if (_grid.SelectedItem is not SenderRow row || !row.CanRevoke) return;

        var result = MessageBox.Show(
            this,
            $"确定撤销「{row.Label}」吗？\n撤销后这台发送端需要重新配对才能继续发送提醒。",
            "撤销发送端",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _registry.Revoke(row.Did);
        RefreshRows();
    }

    private sealed class SenderRow
    {
        public string Label { get; private init; } = "";
        public string Did { get; private init; } = "";
        public string Status { get; private init; } = "";
        public string LastHost { get; private init; } = "";
        public string LastSeenAt { get; private init; } = "";
        public string EnrollBy { get; private init; } = "";
        public string CreatedAt { get; private init; } = "";
        public bool CanRevoke { get; private init; }
        public int SortGroup { get; private init; }
        public long CreatedTicks { get; private init; }

        public static SenderRow From(Device d, long now)
        {
            string status;
            int sortGroup;
            if (d.Revoked)
            {
                status = "已撤销";
                sortGroup = 3;
            }
            else if (d.Confirmed)
            {
                status = "已连接";
                sortGroup = 0;
            }
            else if (d.EnrollBy > 0 && now > d.EnrollBy)
            {
                status = "已过期";
                sortGroup = 2;
            }
            else
            {
                status = "待配对";
                sortGroup = 1;
            }

            var created = ParseTime(d.CreatedAt);
            return new SenderRow
            {
                Label = string.IsNullOrWhiteSpace(d.Label) ? d.Did : d.Label,
                Did = d.Did,
                Status = status,
                LastHost = string.IsNullOrWhiteSpace(d.LastHost) ? "-" : d.LastHost,
                LastSeenAt = FormatTime(d.LastSeenAt),
                EnrollBy = d.EnrollBy > 0 ? FormatUnix(d.EnrollBy) : "不限",
                CreatedAt = FormatTime(d.CreatedAt),
                CanRevoke = !d.Revoked,
                SortGroup = sortGroup,
                CreatedTicks = created?.UtcTicks ?? 0,
            };
        }

        private static string FormatUnix(long unixSeconds)
            => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        private static string FormatTime(string value)
        {
            var t = ParseTime(value);
            return t is null ? "-" : t.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static DateTimeOffset? ParseTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed;
            return null;
        }
    }
}
