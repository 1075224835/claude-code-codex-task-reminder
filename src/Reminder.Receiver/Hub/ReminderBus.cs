using Reminder.Protocol.Dtos;

namespace Reminder.Receiver.Hub;

/// <summary>Hub（后台线程）与 UI（WPF 线程）之间的进程内事件总线。</summary>
public interface IReminderBus
{
    event Action<ReminderMessage>? ReminderReceived;
    event Action<string>? AckReceived; // 参数为提醒 id

    void PublishReminder(ReminderMessage m);
    void PublishAck(string id);
}

public sealed class ReminderBus : IReminderBus
{
    public event Action<ReminderMessage>? ReminderReceived;
    public event Action<string>? AckReceived;

    public void PublishReminder(ReminderMessage m) => ReminderReceived?.Invoke(m);
    public void PublishAck(string id) => AckReceived?.Invoke(id);
}
