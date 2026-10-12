using ReMinder.App.Platform;
using ReMinder.App.Services;
using ReMinder.App.UI;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace ReMinder.App;

internal sealed class CoreCoordinator : IDisposable
{
    private readonly StateStore store; private readonly TaskService tasks; private readonly NotificationService notifications = new();
    private readonly ReminderService reminders; private readonly PanelSupervisor panel;
    private TrayService? tray; private bool ready, exiting;
    private string lastMode = "";
    private SettingsWindow? settingsWindow; private LibraryWindow? library; private ReminderWindow? reminderWindow; private FrameWindow? menu;
    private readonly Dictionary<string, EditorWindow> editors = new();
    private readonly Queue<Dictionary<string, string>> activationQueue = new();
    public CoreCoordinator(string directory, bool qa)
    {
        store = new StateStore(directory); tasks = new TaskService(store); reminders = new ReminderService(store, notifications); panel = new PanelSupervisor(store, directory, qa);
        Log.DirectoryPath = directory; notifications.Invoked += OnNotification; panel.Command += message => Controls.Safely(() => Execute(message)); reminders.Error += text => Toast(text);
        panel.StatusChanged += () => settingsWindow?.UpdateHostStatus(panel.Status);
    }
    public async Task StartAsync()
    {
        await store.LoadAsync();
        if (!store.ReadOnly) await store.CommitAsync(s =>
        {
            var changed = false; var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
            var expired = s.Tasks.Where(t => t.Status == TaskStatus.Deleted && t.DeletedAtUtc < cutoff).Select(t => t.Id).ToHashSet();
            if (expired.Count > 0) { s.Tasks.RemoveAll(t => expired.Contains(t.Id)); s.Reminders.RemoveAll(e => expired.Contains(e.TaskId)); changed = true; }
            for (var i = 0; i < s.Reminders.Count; i++) if (s.Reminders[i].DeliveryState == DeliveryState.Prepared) { s.Reminders[i] = s.Reminders[i] with { DeliveryState = DeliveryState.Unknown, Reason = "上次提交结果不确定，未自动重复发送。" }; changed = true; }
            return changed;
        });
        Theme.Apply(store.Snapshot.Settings); notifications.Register();
        // Register before reading activation. COM launches may instead deliver NotificationInvoked later.
        try { var activation = AppInstance.GetCurrent().GetActivatedEventArgs(); if (activation.Kind == ExtendedActivationKind.AppNotification && activation.Data is AppNotificationActivatedEventArgs args) notifications.Handle(args); }
        catch (Exception e) { Log.Write("activation-read", e); }
        tray = new TrayService(); tray.Command += action => Controls.Safely(() => Execute(new WireMessage { Action = action })); tray.ShellRestarted += panel.Retry;
        tray.EnvironmentChanged += () => { Theme.Apply(store.Snapshot.Settings); panel.Refresh(); reminders.Reconcile(); };
        lastMode = store.Snapshot.Settings.WindowMode; store.Changed += Changed; Changed(); panel.Start(); reminders.Start(); ready = true;
        if (!string.IsNullOrEmpty(store.RecoveryNotice)) Toast(store.RecoveryNotice);
        while (activationQueue.TryDequeue(out var activation)) Controls.Safely(() => NotificationAction(activation));
    }
    private void Changed()
    {
        if (!Application.Current.Dispatcher.CheckAccess()) { Application.Current.Dispatcher.BeginInvoke(Changed); return; }
        var settings = store.Snapshot.Settings; Theme.Apply(settings);
        if (tray is not null) { tray.PanelVisible = settings.PanelVisible; tray.Topmost = settings.WindowMode == "topmost"; }
        if (lastMode != settings.WindowMode) { lastMode = settings.WindowMode; panel.Retry(); } else panel.Refresh();
    }
    public async Task Execute(WireMessage message)
    {
        if (exiting) return;
        Log.Write("command-" + message.Action);
        switch (message.Action)
        {
            case "new": OpenEditor(null); break;
            case "edit": var task = store.Snapshot.Tasks.FirstOrDefault(t => t.Id == message.TaskId && t.Status != TaskStatus.Deleted); if (task is not null) OpenEditor(task); else Toast("任务已不存在或已删除"); break;
            case "complete": await tasks.SetStatusAsync(message.TaskId, TaskStatus.Completed, message.ExpectedVersion); Toast("任务已完成", true); break;
            case "undo": await tasks.UndoAsync(); Toast("已撤销"); break;
            case "history": OpenHistory(); break;
            case "reminders": OpenReminders(); break;
            case "settings": OpenSettings(); break;
            case "menu": OpenMenu(); break;
            case "collapse": case "expand": await Settings(s => s with { Collapsed = message.Action == "collapse" }); menu?.Close(); break;
            case "visibility": await Settings(s => s with { PanelVisible = !s.PanelVisible }); break;
            case "show": if (!panel.Running) panel.Retry(); await Settings(s => s with { PanelVisible = true }); await panel.SendAsync(new WireMessage { Kind = "focus" }); break;
            case "topmost": await Settings(s => s with { WindowMode = s.WindowMode == "topmost" ? "desktop" : "topmost" }); menu?.Close(); break;
            case "retry-desktop": panel.Retry(); menu?.Close(); break;
            case "exit": Application.Current.Shutdown(); break;
        }
    }
    private Task Settings(Func<AppSettings, AppSettings> update) => store.CommitAsync(s => { var settings = update(s.Settings); if (settings == s.Settings) return false; s.Settings = settings; return true; });
    private void Show(Window window) { PlacementService.Input(window, store.Snapshot.Settings); window.Show(); Native.ShowWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle, 9); window.Activate(); }
    private void OpenEditor(DeadlineTask? task)
    {
        var key = task?.Id ?? "new"; if (editors.TryGetValue(key, out var existing)) { existing.Activate(); return; }
        var editor = new EditorWindow(tasks, store.Snapshot.Settings, task); editors.Add(key, editor); editor.Closed += (_, _) => { editors.Remove(key); if (editor.WasDeleted) Toast("任务已移入最近删除", true); else if (editor.WasSaved) Toast(task is null ? "任务已添加" : "任务已保存"); }; Show(editor);
    }
    private void OpenHistory() { if (library is not null) { library.Activate(); return; } library = new LibraryWindow(store, tasks, OpenEditor, Toast); library.Closed += (_, _) => library = null; Show(library); }
    private void OpenReminders() { if (reminderWindow is not null) { reminderWindow.Activate(); return; } reminderWindow = new ReminderWindow(store, reminders, OpenEditor); reminderWindow.Closed += (_, _) => reminderWindow = null; Show(reminderWindow); }
    private void OpenSettings() { if (settingsWindow is not null) { settingsWindow.Activate(); return; } settingsWindow = new SettingsWindow(store, tasks, notifications, panel.Status); settingsWindow.Closed += (_, _) => settingsWindow = null; Show(settingsWindow); }
    private void OpenMenu()
    {
        if (menu is not null) { menu.Activate(); return; }
        var menuWindow = new MenuWindow(store.Snapshot.Settings, action => Controls.Safely(async () => { menu?.Close(); await Execute(new WireMessage { Action = action }); })); menu = menuWindow; menu.Closed += (_, _) => menu = null; Show(menu);
    }
    private void Toast(string text, bool undo = false) => _ = panel.SendAsync(new WireMessage { Kind = "toast", Text = text, CanUndo = undo });
    private void OnNotification(Dictionary<string, string> args) => Application.Current.Dispatcher.BeginInvoke(() => { if (ready) Controls.Safely(() => NotificationAction(args), text => Toast(text)); else activationQueue.Enqueue(args); });
    private async Task NotificationAction(Dictionary<string, string> args)
    {
        var action = args.GetValueOrDefault("action", "open"); if (action is "reminders" or "settings") { await Execute(new WireMessage { Action = action }); return; }
        var task = store.Snapshot.Tasks.FirstOrDefault(t => t.Id == args.GetValueOrDefault("taskId"));
        if (task is null || task.Status != TaskStatus.Pending || !int.TryParse(args.GetValueOrDefault("revision"), out var revision) || task.ScheduleRevision != revision) { Toast("这次提醒已失效，任务已更新或处理。"); return; }
        if (action == "open") { await Settings(s => s with { PanelVisible = true }); await panel.SendAsync(new WireMessage { Kind = "focus", TaskId = task.Id }); OpenEditor(task); return; }
        var item = store.Snapshot.Reminders.FirstOrDefault(e => e.Id == args.GetValueOrDefault("eventId") && e.TaskId == task.Id && e.ScheduleRevision == revision);
        if (item is null || !int.TryParse(args.GetValueOrDefault("attempt"), out var attempt) || item.Attempts != attempt) { Toast("这次提醒已更新，请从提醒记录中操作。"); return; }
        if (action == "snooze10") await reminders.SnoozeAsync(item.Id, attempt);
        else if (action == "complete") { await tasks.SetStatusAsync(task.Id, TaskStatus.Completed, task.UpdatedAtUtc); await notifications.RemoveAsync(item.Tag); Toast("任务已完成", true); }
    }
    public void Dispose() { exiting = true; store.Changed -= Changed; reminders.Dispose(); panel.Dispose(); tray?.Dispose(); notifications.Dispose(); }
}

internal sealed class MenuWindow : FrameWindow
{
    public MenuWindow(AppSettings settings, Action<string> action) : base("面板菜单", 280)
    {
        foreach (var (label, command, icon) in new[] { ("收起面板", "collapse", "up"), ("新建任务", "new", "plus"), ("任务记录", "history", "check"), ("提醒记录", "reminders", "bell"), (settings.WindowMode == "topmost" ? "固定在桌面" : "始终置顶", "topmost", "pin"), ("重试桌面固定", "retry-desktop", "restore"), ("设置", "settings", "settings"), ("隐藏面板", "visibility", "close"), ("退出 Re:minder", "exit", "close") })
        { var cmd = command; var b = Controls.Button(label, () => action(cmd), null, icon); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 2, 0, 2); Body.Children.Add(b); }
    }
}
