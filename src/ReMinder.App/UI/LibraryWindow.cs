namespace ReMinder.App.UI;

internal sealed class LibraryWindow : FrameWindow
{
    private readonly StateStore store;
    private readonly TaskService tasks;
    private readonly Action<DeadlineTask> edit;
    private readonly StackPanel list = new();
    private readonly TextBox search = new() { ToolTip = "搜索任务名称和备注", Margin = new Thickness(0, 8, 0, 12) };
    private readonly ComboBox filter = new() { MinHeight = 40, ItemsSource = new[] { "已完成", "最近删除（保留 30 天）", "全部任务" }, SelectedIndex = 0 };
    private readonly Action<string, bool> toast;
    public LibraryWindow(StateStore store, TaskService tasks, Action<DeadlineTask> edit, Action<string, bool> toast) : base("任务记录", 460)
    {
        this.store = store; this.tasks = tasks; this.edit = edit; this.toast = toast;
        Body.Children.Add(filter); Body.Children.Add(search); Body.Children.Add(list); search.TextChanged += (_, _) => Refresh(); filter.SelectionChanged += (_, _) => { Refresh(); if (IsLoaded) Motion.Appear(list, Motion.Content, 4); };
        store.Changed += Changed; Closed += (_, _) => store.Changed -= Changed; Refresh();
    }
    private void Changed() => Dispatcher.InvokeAsync(Refresh);
    private void Refresh()
    {
        list.Children.Clear(); var state = store.Snapshot;
        var items = state.Tasks.Where(t => filter.SelectedIndex == 0 ? t.Status == TaskStatus.Completed : filter.SelectedIndex == 1 ? t.Status == TaskStatus.Deleted : t.Status != TaskStatus.Deleted).Where(t => t.Title.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase) || t.Notes.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(t => t.UpdatedAtUtc).ToArray();
        if (items.Length == 0) list.Children.Add(Controls.Text("这里还没有记录。", "Small"));
        foreach (var task in items)
        {
            var row = new StackPanel(); row.Children.Add(Controls.Text(task.Title, "Title")); row.Children.Add(Controls.Text(DeadlineClock.Format(task.DeadlineUtc, DateTimeOffset.UtcNow).Absolute, "Small"));
            if (!string.IsNullOrWhiteSpace(task.Notes)) row.Children.Add(new TextBlock { Text = task.Notes, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) });
            var actions = new WrapPanel();
            if (task.Status != TaskStatus.Deleted) actions.Children.Add(Controls.Button("编辑", () => edit(task)));
            if (task.Status != TaskStatus.Pending) actions.Children.Add(Controls.Button(task.Status == TaskStatus.Deleted ? "恢复" : "重新打开", () => Controls.Safely(async () => { await tasks.SetStatusAsync(task.Id, task.Status == TaskStatus.Deleted ? task.StatusBeforeDelete ?? TaskStatus.Pending : TaskStatus.Pending, task.UpdatedAtUtc); toast("任务已恢复", true); }), "SecondaryButton", "restore"));
            if (task.Status != TaskStatus.Deleted) actions.Children.Add(Controls.Button("删除", () => Controls.Safely(async () => { await tasks.SetStatusAsync(task.Id, TaskStatus.Deleted, task.UpdatedAtUtc); toast("任务已移入最近删除", true); }), null, "trash"));
            row.Children.Add(actions); var card = Controls.Card(row); card.Margin = new Thickness(0, 0, 0, 8); list.Children.Add(card);
        }
    }
}
