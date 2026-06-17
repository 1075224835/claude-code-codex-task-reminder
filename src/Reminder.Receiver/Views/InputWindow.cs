using System.Windows;
using System.Windows.Controls;

namespace Reminder.Receiver.Views;

/// <summary>简单的单行输入对话框（代码构建）。ShowDialog()==true 时取 Value。</summary>
public sealed class InputWindow : Window
{
    private readonly TextBox _box;
    public string Value => _box.Text.Trim();

    public InputWindow(string title, string prompt, string initial)
    {
        Title = title;
        Width = 500;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lbl = new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(lbl, 0);
        grid.Children.Add(lbl);

        _box = new TextBox { Text = initial, FontSize = 14, Padding = new Thickness(4) };
        Grid.SetRow(_box, 1);
        grid.Children.Add(_box);

        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "确定", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => { DialogResult = true; };
        var cancel = new Button { Content = "取消", Width = 90, Height = 30, IsCancel = true };
        panel.Children.Add(ok);
        panel.Children.Add(cancel);
        Grid.SetRow(panel, 2);
        grid.Children.Add(panel);

        Content = grid;
    }
}
