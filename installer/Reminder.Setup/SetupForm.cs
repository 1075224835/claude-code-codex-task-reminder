using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Reminder.Setup;

public sealed class SetupForm : Form
{
    private readonly CheckBox _recv;
    private readonly CheckBox _sender;
    private readonly CheckBox _claudeHooks;
    private readonly Label _status;
    private readonly Button _install;
    private readonly Button _uninstall;

    public SetupForm()
    {
        Text = "全屏提醒 安装程序";
        Width = 540;
        Height = 414;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var title = new Label { Text = "全屏提醒", Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold), Left = 24, Top = 18, AutoSize = true };
        var sub = new Label { Text = "Windows 全屏强制提醒系统（接收端 + 发送端）", Left = 26, Top = 60, AutoSize = true, ForeColor = Color.Gray };

        var grp = new GroupBox { Text = "选择要在这台电脑上安装的组件", Left = 24, Top = 96, Width = 480, Height = 156 };
        _recv = new CheckBox { Text = "接收端 —— 在这台电脑上显示全屏提醒", Left = 18, Top = 30, Width = 440, Checked = true };
        _sender = new CheckBox { Text = "发送端 —— 这台电脑运行 Claude Code / Codex，发出提醒", Left = 18, Top = 64, Width = 440, Checked = true };
        _claudeHooks = new CheckBox { Text = "└ 接入 Claude Code（自动写入钩子；Codex 无需此项）", Left = 38, Top = 98, Width = 430, Checked = true };
        _sender.CheckedChanged += (_, _) => _claudeHooks.Enabled = _sender.Checked;
        grp.Controls.AddRange(new Control[] { _recv, _sender, _claudeHooks });

        var hint = new Label { Text = "安装为「每用户」，无需管理员。需已装 .NET 8 桌面+ASP.NET Core 运行时。", Left = 26, Top = 262, AutoSize = true, ForeColor = Color.Gray };
        _status = new Label { Left = 26, Top = 286, Width = 480, Height = 40, ForeColor = Color.DimGray };

        _install = new Button { Text = "安装", Left = 288, Top = 334, Width = 100, Height = 38 };
        _install.Click += OnInstall;
        _uninstall = new Button { Text = "卸载", Left = 402, Top = 334, Width = 100, Height = 38 };
        _uninstall.Click += OnUninstall;

        Controls.AddRange(new Control[] { title, sub, grp, hint, _status, _install, _uninstall });
    }

    private void OnInstall(object? sender, EventArgs e)
    {
        if (!_recv.Checked && !_sender.Checked) { _status.Text = "请至少选择一个组件。"; return; }
        SetBusy(true, "正在安装…");
        try
        {
            Installer.Install(_recv.Checked, _sender.Checked, _claudeHooks.Checked && _sender.Checked, m => { _status.Text = m; Application.DoEvents(); });
            MessageBox.Show("安装完成！\n\n" + BuildNext(), "全屏提醒", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _status.Text = "安装完成。可关闭本窗口。";
        }
        catch (Exception ex)
        {
            MessageBox.Show("安装失败：" + ex.Message, "全屏提醒", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "安装失败。";
        }
        finally { SetBusy(false, _status.Text); }
    }

    private void OnUninstall(object? sender, EventArgs e)
    {
        if (MessageBox.Show("确定卸载全屏提醒吗？（配置/密钥默认保留）", "全屏提醒",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SetBusy(true, "正在卸载…");
        try { Installer.Uninstall(false, m => { _status.Text = m; Application.DoEvents(); }); _status.Text = "已卸载。"; }
        catch (Exception ex) { _status.Text = "卸载失败：" + ex.Message; }
        finally { SetBusy(false, _status.Text); }
    }

    private string BuildNext()
    {
        var sb = new StringBuilder();
        if (_recv.Checked)
            sb.AppendLine("• 接收端已在系统托盘运行：右键托盘图标 →「添加发送端」生成配对码；多台发送端可重复添加，并可在「管理发送端」里撤销。");
        if (_sender.Checked)
        {
            sb.AppendLine("• 发送端已在系统托盘运行：右键托盘图标 →「连接到接收端」，对话框自动粘贴配对码，点「连接」。");
            sb.AppendLine("• Codex 桌面端：发送端自动监视会话日志，回合完成即提醒，无需配置。");
            if (_claudeHooks.Checked)
                sb.AppendLine("• Claude Code：钩子已自动写入 ~/.claude/settings.json，重启 Claude Code 后生效。");
            else
                sb.AppendLine("• Claude Code：可在发送端托盘点「接入 Claude Code（配置钩子）」一键配置。");
        }
        return sb.ToString();
    }

    private void SetBusy(bool busy, string msg)
    {
        _install.Enabled = !busy;
        _uninstall.Enabled = !busy;
        _status.Text = msg;
        Application.DoEvents();
    }
}
