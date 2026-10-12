using ReMinder.App.Services;

namespace ReMinder.App.UI;

internal sealed class ReminderWindow : FrameWindow
{
    private readonly StateStore store; private readonly ReminderService service; private readonly Action<DeadlineTask> edit;
    private readonly StackPanel list = new();
    public ReminderWindow(StateStore store, ReminderService service, Action<DeadlineTask> edit) : base("提醒记录", 460)
    {
        this.store = store; this.service = service; this.edit = edit;
        Body.Children.Add(new TextBlock { Text = "系统接受通知不代表你已看到。Windows 的勿扰模式和通知设置决定是否出现横幅。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        Body.Children.Add(list); store.Changed += Changed; Closed += (_, _) => store.Changed -= Changed; Refresh();
    }
    private void Changed() => Dispatcher.InvokeAsync(Refresh);
    private void Refresh()
    {
        list.Children.Clear(); var state = store.Snapshot;
        var events = state.Reminders.Where(e => e.DeliveryState is not DeliveryState.Cancelled and not DeliveryState.Superseded && state.Tasks.Any(t => t.Id == e.TaskId && t.Status == TaskStatus.Pending && t.ScheduleRevision == e.ScheduleRevision)).OrderBy(e => e.DeliveryState is DeliveryState.Unknown or DeliveryState.Failed or DeliveryState.Blocked ? 0 : e.DeliveryState == DeliveryState.Planned ? 2 : 1).ThenBy(e => e.EffectiveTrigger).Take(100).ToArray();
        if (events.Length == 0) list.Children.Add(Controls.Text("当前没有提醒记录。", "Small"));
        foreach (var item in events)
        {
            var task = state.Tasks.First(t => t.Id == item.TaskId); var row = new StackPanel(); row.Children.Add(Controls.Text(task.Title, "Title"));
            row.Children.Add(Controls.Text($"{item.EffectiveTrigger.LocalDateTime:M月d日 HH:mm} · {Label(item.DeliveryState)}", "Small"));
            if (!string.IsNullOrEmpty(item.Reason)) row.Children.Add(new TextBlock { Text = item.Reason, TextWrapping = TextWrapping.Wrap });
            var buttons = new WrapPanel(); buttons.Children.Add(Controls.Button("打开任务", () => edit(task)));
            if (item.DeliveryState is DeliveryState.Blocked or DeliveryState.Failed or DeliveryState.Unknown) buttons.Children.Add(Controls.Button("重新提醒", () => Controls.Safely(() => service.RetryAsync(item.Id)), "SecondaryButton"));
            if (item.DeliveryState == DeliveryState.Accepted) buttons.Children.Add(Controls.Button("稍后 10 分钟", () => Controls.Safely(() => service.SnoozeAsync(item.Id, item.Attempts)), "SecondaryButton"));
            row.Children.Add(buttons); var card = Controls.Card(row); card.Margin = new Thickness(0, 0, 0, 8); list.Children.Add(card);
        }
    }
    private static string Label(DeliveryState s) => s switch { DeliveryState.Planned => "已计划", DeliveryState.Prepared => "正在提交", DeliveryState.Accepted => "系统已接受", DeliveryState.Snoozed => "已延后", DeliveryState.Processed => "已处理", DeliveryState.Blocked => "通知不可用", DeliveryState.Failed => "提交失败", DeliveryState.Unknown => "上次结果不确定", _ => s.ToString() };
}
