using System.Drawing;
using System.Windows.Forms;

namespace Reminder.Sender.Tray;

/// <summary>「连接到接收端」对话框：粘贴配对码，无需终端。</summary>
public sealed class ConnectDialog : Form
{
    private readonly TextBox _box;

    public string Code => _box.Text.Trim();

    public ConnectDialog()
    {
        Text = "连接到接收端";
        Width = 580;
        Height = 340;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var lbl = new Label
        {
            Text = "粘贴接收端「添加发送端」生成的配对码，然后点击「连接」：",
            Left = 16, Top = 14, Width = 540, AutoSize = true,
        };
        _box = new TextBox
        {
            Left = 16, Top = 42, Width = 540, Height = 170,
            Multiline = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9f), WordWrap = true,
        };
        var paste = new Button { Text = "从剪贴板粘贴", Left = 16, Top = 224, Width = 130, Height = 34 };
        paste.Click += (_, _) => { try { if (Clipboard.ContainsText()) _box.Text = Clipboard.GetText(); } catch { } };

        var ok = new Button { Text = "连接", Left = 332, Top = 224, Width = 104, Height = 34, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Left = 452, Top = 224, Width = 104, Height = 34, DialogResult = DialogResult.Cancel };

        Controls.AddRange(new Control[] { lbl, _box, paste, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;

        // 自动预填：剪贴板里若有较长文本就直接填上（解析端会自动从中提取配对码）
        try
        {
            if (Clipboard.ContainsText())
            {
                string t = Clipboard.GetText().Trim();
                if (t.Length is > 40 and < 12000) _box.Text = t;
            }
        }
        catch { /* 忽略 */ }
    }
}
