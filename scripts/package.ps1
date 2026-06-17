$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

function Reset-Directory([string] $relativePath) {
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $root $relativePath))
    if (-not $fullPath.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "路径越界：$fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fullPath | Out-Null
}

Reset-Directory "build\payload-staging"
Reset-Directory "全屏提醒-安装包"

dotnet publish src\Reminder.Receiver\Reminder.Receiver.csproj -c Release -o build\payload-staging\FullscreenReminder -p:DebugType=None -p:DebugSymbols=false
dotnet publish src\Reminder.Sender\Reminder.Sender.csproj -c Release -o build\payload-staging\reminder-agent -p:DebugType=None -p:DebugSymbols=false

Get-ChildItem -LiteralPath build\payload-staging -Recurse -Filter *.pdb | Remove-Item -Force

$payload = [System.IO.Path]::GetFullPath((Join-Path $root "installer\Reminder.Setup\payload.zip"))
if (-not $payload.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "路径越界：$payload"
}
if (Test-Path -LiteralPath $payload) {
    Remove-Item -LiteralPath $payload -Force
}
Compress-Archive -Path build\payload-staging\* -DestinationPath $payload -Force

dotnet publish installer\Reminder.Setup\Reminder.Setup.csproj -c Release -o 全屏提醒-安装包 -p:DebugType=None -p:DebugSymbols=false

$readme = @'
全屏提醒 安装说明

1. 双击 FullscreenReminder-Setup.exe。
2. 按界面选择安装接收端、发送端，默认会写入当前用户开机自启。
3. 接收端托盘里选择「添加发送端」，把配对码粘贴到发送端的「连接到接收端」。
4. 多台发送端请重复执行「添加发送端」；可在接收端托盘「管理发送端」查看状态或撤销单台发送端。
5. 如需微信推送：接收端托盘选择「设置 Server酱 推送(微信)」。

安全说明

- 新版配对码只包含一次性登记密钥，不再携带全局主密钥。
- 配对码有效期约 10 分钟，登记成功后会立即作废。
- 每台发送端都有独立设备编号和消息密钥，撤销单台发送端不会影响其他发送端。
- Server 酱推送会把通知内容发送到第三方服务，请只在你接受该外发范围时启用。

卸载

再次运行 FullscreenReminder-Setup.exe 选择卸载；默认保留配置和密钥。
如需彻底清理，请使用命令行运行：FullscreenReminder-Setup.exe /uninstall /purge
'@
Set-Content -Encoding UTF8 -LiteralPath "全屏提醒-安装包\使用说明.txt" -Value $readme

$zip = [System.IO.Path]::GetFullPath((Join-Path $root "全屏提醒-安装包.zip"))
if (-not $zip.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "路径越界：$zip"
}
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path 全屏提醒-安装包\* -DestinationPath $zip -Force

Write-Host "安装包已生成：$zip"
