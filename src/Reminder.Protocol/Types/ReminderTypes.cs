namespace Reminder.Protocol.Types;

/// <summary>
/// 消息类型的规范 key（线协议中的 type 字段值）。两端共享这套语义。
/// 类型集可在接收端配置中增删，但内置默认集保证开箱即用，也是统计维度。
/// </summary>
public static class ReminderTypes
{
    public const string TaskComplete = "task_complete";
    public const string NeedsInput = "needs_input";
    public const string NeedsApproval = "needs_approval";
    public const string Error = "error";
    public const string Info = "info";

    /// <summary>内置默认类型集（统计/配置默认枚举顺序）。</summary>
    public static readonly string[] Defaults =
    {
        TaskComplete, NeedsInput, NeedsApproval, Error, Info,
    };

    /// <summary>默认显示名（用户可在两端各自覆盖）。</summary>
    public static string DefaultDisplayName(string key) => key switch
    {
        TaskComplete => "任务完成",
        NeedsInput   => "需要输入/更多信息",
        NeedsApproval=> "需要授权/审批",
        Error        => "出错/失败",
        Info         => "一般信息",
        _            => key,
    };

    /// <summary>默认提醒文字模板。占位符：{host} {project} {cwd} {path} {session} {agent} {detail} {time}。</summary>
    public static string DefaultTemplate(string key) => key switch
    {
        TaskComplete => "{host} · {project}\n任务已完成",
        NeedsInput   => "{host} · {project}\n需要你的输入：{detail}",
        NeedsApproval=> "{host} · {project}\n等待授权：{detail}",
        Error        => "{host} · {project}\n出错了：{detail}",
        Info         => "{host} · {project}\n{detail}",
        _            => "{host} · {project}\n{detail}",
    };

    /// <summary>默认强调色（十六进制 #RRGGBB）。</summary>
    public static string DefaultColor(string key) => key switch
    {
        TaskComplete => "#2E7D32", // 绿
        NeedsInput   => "#1565C0", // 蓝
        NeedsApproval=> "#E65100", // 橙
        Error        => "#C62828", // 红
        Info         => "#455A64", // 灰蓝
        _            => "#37474F",
    };
}
