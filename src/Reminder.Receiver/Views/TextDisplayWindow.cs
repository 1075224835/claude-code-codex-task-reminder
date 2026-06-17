using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Reminder.Receiver.Views;

/// <summary>通用只读文本窗口（用于展示配对码、统计等），代码构建，避免额外 XAML。</summary>
public sealed class TextDisplayWindow : Window
{
    public TextDisplayWindow(string title, string content, bool copyButton = true)
    {
        Title = title;
        Width = 760;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var box = new TextBox
        {
            Text = content,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
        };
        Grid.SetRow(box, 0);
        grid.Children.Add(box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        if (copyButton)
        {
            var copy = new Button { Content = "复制", Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(0, 0, 8, 0) };
            copy.Click += (_, _) => { try { Clipboard.SetText(content); } catch { /* 忽略 */ } };
            buttons.Children.Add(copy);
        }
        var close = new Button { Content = "关闭", Padding = new Thickness(20, 6, 20, 6) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
    }
}
