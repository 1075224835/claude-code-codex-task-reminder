using Reminder.Sender.HookParsers;

int passed = 0, failed = 0;
void Check(string name, bool ok)
{
    if (ok) { passed++; Console.WriteLine($"  PASS  {name}"); }
    else { failed++; Console.WriteLine($"  FAIL  {name}"); }
}

Console.WriteLine("== Reminder.Sender 测试 ==");

Check("顶层用户任务保留提醒", !Hooks.IsCodexSubagent(
    """{"type":"session_meta","payload":{"id":"main","thread_source":"user","source":"vscode"}}"""));

Check("thread_source 标记的子智能体被忽略", Hooks.IsCodexSubagent(
    """{"type":"session_meta","payload":{"id":"child","thread_source":"subagent","source":"vscode"}}"""));

Check("source.subagent 标记的子智能体被忽略", Hooks.IsCodexSubagent(
    """{"type":"session_meta","payload":{"id":"child","source":{"subagent":{"thread_spawn":{"parent_thread_id":"main","depth":1}}}}}"""));

Check("备用 notify 结构中的子智能体被忽略", Hooks.IsCodexSubagent(
    """{"thread-id":"child","thread-source":"subagent","cwd":"C:\\repo"}"""));

Check("损坏的事件不会误判为子智能体", !Hooks.IsCodexSubagent("not-json"));

Console.WriteLine();
Console.WriteLine($"结果：{passed} 通过, {failed} 失败");
return failed == 0 ? 0 : 1;
