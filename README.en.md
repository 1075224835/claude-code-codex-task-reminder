# Fullscreen Reminder

[中文说明](README.md)

Fullscreen Reminder is a Windows reminder tool built around a receiver and one or more senders. Senders watch task activity from tools such as Claude Code and Codex, while the receiver displays full-screen alerts on the Windows desktop. Optional ServerChan forwarding can also push notifications to WeChat.

## Features

- Tray-based receiver that displays incoming reminders as full-screen overlays.
- Multiple sender support, with a separate device ID and message key for each sender.
- Short-lived pairing codes that contain only a one-time enrollment secret.
- Pairing codes expire after about 10 minutes and are invalidated after successful enrollment.
- Message type filtering, auto-close countdown, custom background image, and multi-monitor selection.
- Optional ServerChan forwarding with task command, time, project, and path details.

## Project Layout

- `src/Reminder.Receiver`: Receiver app, embedded Hub, tray menu, full-screen overlay, and sender management.
- `src/Reminder.Sender`: Sender app, tray program, Codex session watcher, Claude Code hook support, and fallback outbox.
- `src/Reminder.Protocol`: Encrypted envelopes, pairing code format, message types, and protocol DTOs.
- `installer/Reminder.Setup`: Installer project.
- `tests/Reminder.Protocol.Tests`: Protocol and encryption tests.
- `scripts/package.ps1`: Packaging script for rebuilding the installer bundle.

## Build

Install the .NET 8 SDK with Windows desktop workload support.

```powershell
dotnet build Reminder.sln
dotnet run --project tests\Reminder.Protocol.Tests\Reminder.Protocol.Tests.csproj
```

To build an installer package:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package.ps1
```

The generated package is `全屏提醒-安装包.zip`.

## Security Notes

- Do not commit local configuration, device registries, private certificates, logs, or pairing codes.
- Receiver and sender configuration files are protected locally and should not be stored in the repository.
- ServerChan forwarding sends notification content to a third-party service; enable it only if that data sharing is acceptable.
- Publishing the source code does not expose your computer to remote access. A sender still needs a valid pairing code and authenticated encrypted messages.

## License

This project is licensed under the MIT License.
