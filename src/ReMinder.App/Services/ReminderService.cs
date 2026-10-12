using System.Windows.Threading;

namespace ReMinder.App.Services;

internal sealed class ReminderService : IDisposable
{
    private readonly StateStore store;
    private readonly NotificationService notifications;
    private readonly DispatcherTimer timer = new();
    private readonly SemaphoreSlim processing = new(1, 1);
    private bool stopped;
    private DateTimeOffset backoffUntil;
    public event Action<string>? Error;
    public ReminderService(StateStore stateStore, NotificationService notificationService)
    {
        store = stateStore; notifications = notificationService;
        timer.Tick += (_, _) => ControlsRun();
        store.Changed += Reschedule;
    }
    public void Start() { Reschedule(); ControlsRun(); }
    public void Reconcile() => ControlsRun();
    private async void ControlsRun()
    {
        if (stopped || !await processing.WaitAsync(0)) return;
        try { await DispatchDueAsync(); }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { backoffUntil = DateTimeOffset.UtcNow.AddSeconds(30); Log.Write("reminder-check", e); Error?.Invoke("提醒检查未能完成，任务数据已保留。请查看设置与待处理提醒。"); }
        finally { processing.Release(); if (!stopped) Reschedule(); }
    }
    private async Task DispatchDueAsync()
    {
        if (store.ReadOnly) return;
        var due = ReminderRules.Due(store.Snapshot, DateTimeOffset.UtcNow);
        if (due.Length == 0) return;
        var relevant = ReminderRules.Relevant(due);
        var selected = relevant.Select(e => e.Id).ToHashSet();
        await store.CommitAsync(s =>
        {
            var changed = false;
            for (var i = 0; i < s.Reminders.Count; i++)
                if (due.Any(e => e.Id == s.Reminders[i].Id) && !selected.Contains(s.Reminders[i].Id)) { s.Reminders[i] = s.Reminders[i] with { DeliveryState = DeliveryState.Superseded, Reason = "已合并为本任务最新相关提醒" }; changed = true; }
            return changed;
        });
        if (!notifications.CanSend)
        {
            await store.CommitAsync(s =>
            {
                for (var i = 0; i < s.Reminders.Count; i++) if (selected.Contains(s.Reminders[i].Id)) s.Reminders[i] = s.Reminders[i] with { DeliveryState = DeliveryState.Blocked, Reason = notifications.Status };
                return true;
            });
            return;
        }
        // More than three overdue tasks produce one recovery summary.
        var groups = relevant.Length > 3 ? new[] { relevant } : relevant.Select(e => new[] { e }).ToArray();
        foreach (var group in groups) await SendGroupAsync(group);
    }

    private async Task SendGroupAsync(ReminderEvent[] group)
    {
        var ids = group.Select(e => e.Id).ToHashSet(); var tag = NotificationService.Tag(ids); var attempt = Guid.NewGuid().ToString("N");
        await store.CommitAsync(s =>
        {
            foreach (var id in ids)
            {
                var i = s.Reminders.FindIndex(e => e.Id == id); if (i < 0) continue;
                var old = s.Reminders[i];
                if (!s.Tasks.Any(t => t.Id == old.TaskId && t.Status == TaskStatus.Pending && t.ScheduleRevision == old.ScheduleRevision)) continue;
                s.Reminders[i] = old with { DeliveryState = DeliveryState.Prepared, AttemptId = attempt, LastAttemptAtUtc = DateTimeOffset.UtcNow, Attempts = old.Attempts + 1, Tag = tag };
            }
            return true;
        });
        var snapshot = store.Snapshot;
        var ready = snapshot.Reminders.Where(e => ids.Contains(e.Id) && e.DeliveryState == DeliveryState.Prepared && e.AttemptId == attempt).ToArray();
        if (ready.Length == 0) return;
        uint notificationId;
        try { notificationId = notifications.Send(snapshot, ready, tag); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            await store.CommitAsync(s =>
            {
                for (var i = 0; i < s.Reminders.Count; i++) if (s.Reminders[i].AttemptId == attempt)
                {
                    var e = s.Reminders[i]; s.Reminders[i] = e with { DeliveryState = DeliveryState.Failed, FailureCount = e.FailureCount + 1, RetryAtUtc = DateTimeOffset.UtcNow.AddSeconds(30 * (e.FailureCount + 1)), Reason = "系统投递失败，可在待处理提醒中重试。" };
                }
                return true;
            });
            Log.Write("notification-send", error); return;
        }
        // Once Show succeeded, a write failure must never cause an immediate resend.
        await store.CommitAsync(s =>
        {
            for (var i = 0; i < s.Reminders.Count; i++) if (s.Reminders[i].AttemptId == attempt && s.Reminders[i].DeliveryState == DeliveryState.Prepared) s.Reminders[i] = s.Reminders[i] with { DeliveryState = DeliveryState.Accepted, NotificationId = notificationId, FailureCount = 0, RetryAtUtc = null, Reason = "" };
            return true;
        });
    }

    public async Task SnoozeAsync(string id, int expectedAttempt)
    {
        string tag = "";
        await store.CommitAsync(s =>
        {
            var i = s.Reminders.FindIndex(e => e.Id == id); if (i < 0) throw new InvalidOperationException("这次提醒已经失效。");
            var e = s.Reminders[i];
            if (!s.Tasks.Any(t => t.Id == e.TaskId && t.Status == TaskStatus.Pending && t.ScheduleRevision == e.ScheduleRevision)) throw new InvalidOperationException("任务已更新，这次提醒已经失效。");
            if (e.LastSnoozedAttempt == expectedAttempt) return false;
            if (e.Attempts != expectedAttempt || e.DeliveryState is DeliveryState.Cancelled or DeliveryState.Superseded or DeliveryState.Processed) throw new InvalidOperationException("这次提醒已更新，请从待处理列表操作。");
            tag = e.Tag;
            s.Reminders[i] = e with { DeliveryState = DeliveryState.Snoozed, SnoozedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(10), SnoozeCount = e.SnoozeCount + 1, LastSnoozedAttempt = expectedAttempt, FailureCount = 0, Reason = "" };
            return true;
        });
        await notifications.RemoveAsync(tag);
    }

    public Task RetryAsync(string id) => store.CommitAsync(s =>
    {
        var i = s.Reminders.FindIndex(e => e.Id == id); if (i < 0) return false;
        var e = s.Reminders[i];
        if (!s.Tasks.Any(t => t.Id == e.TaskId && t.Status == TaskStatus.Pending && t.ScheduleRevision == e.ScheduleRevision)) throw new InvalidOperationException("任务已更新，这次提醒已经失效。");
        s.Reminders[i] = e with { DeliveryState = DeliveryState.Snoozed, SnoozedUntilUtc = DateTimeOffset.UtcNow.AddSeconds(1), FailureCount = 0, Reason = "" };
        return true;
    });

    private void Reschedule()
    {
        if (!Application.Current.Dispatcher.CheckAccess()) { Application.Current.Dispatcher.BeginInvoke(Reschedule); return; }
        timer.Stop(); if (stopped) return;
        var now = DateTimeOffset.UtcNow;
        var next = store.Snapshot.Reminders.Where(e => e.DeliveryState is DeliveryState.Planned or DeliveryState.Snoozed || e.DeliveryState == DeliveryState.Failed && e.FailureCount < 3).Select(e => e.EffectiveTrigger).DefaultIfEmpty(now.AddMinutes(5)).Min();
        if (next < backoffUntil) next = backoffUntil;
        timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp((next - now).TotalMilliseconds, 250, 300000)); timer.Start();
    }
    public void Dispose() { stopped = true; timer.Stop(); store.Changed -= Reschedule; }
}
