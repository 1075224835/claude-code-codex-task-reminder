# 全屏提醒

Windows 全屏提醒工具，由接收端和发送端组成。发送端监听 Claude Code / Codex 等任务状态，接收端在本机显示全屏提醒，也可选择通过 Server 酱推送到微信。

## 功能

- 接收端托盘常驻，收到提醒后覆盖指定屏幕显示。
- 支持多个发送端，每台发送端使用独立设备编号和独立消息密钥。
- 配对码只包含一次性登记密钥，有效期约 10 分钟，登记成功后立即作废。
- 支持按消息类型过滤、自动关闭倒计时、背景图片和多屏选择。
- 可选 Server 酱推送，详情会包含最近指令、时间、项目和路径等信息。

## 项目结构

- `src/Reminder.Receiver`：接收端，包含内嵌 Hub、托盘菜单、全屏提醒窗口和发送端管理。
- `src/Reminder.Sender`：发送端，包含托盘程序、Codex 会话监视、Claude Code 钩子和发送队列。
- `src/Reminder.Protocol`：加密信封、配对码、消息类型和协议结构。
- `installer/Reminder.Setup`：安装器工程。
- `tests/Reminder.Protocol.Tests`：协议与加密测试。
- `scripts/package.ps1`：重新生成安装包。

## 构建

需要安装 .NET 8 桌面运行时和开发工具。

```powershell
dotnet build Reminder.sln
dotnet run --project tests\Reminder.Protocol.Tests\Reminder.Protocol.Tests.csproj
```

生成安装包：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package.ps1
```

生成结果为 `全屏提醒-安装包.zip`。

## 安全说明

- 不要提交本机运行目录里的配置、设备登记表、证书私钥、日志或配对码。
- 接收端配置和发送端配置会在本机以系统保护方式保存，不应放入仓库。
- Server 酱推送会把通知内容发送到第三方服务，请只在接受该外发范围时启用。
- 公开源码不等于开放你的电脑连接权限；实际连接仍需要有效配对码和认证消息。

## 许可证

本项目使用 MIT 许可证。
