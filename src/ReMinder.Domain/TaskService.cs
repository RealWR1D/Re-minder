using System.Text.Json;

namespace ReMinder.Domain;

public sealed class TaskService(StateStore store, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private DeadlineTask? undoOriginal;
    private string? undoId;
    private DateTimeOffset undoVersion;

    public async Task SaveAsync(DeadlineTask input, DateTimeOffset? expectedVersion)
    {
        Validation.Task(input);
        await store.CommitAsync(s =>
        {
            var index = s.Tasks.FindIndex(t => t.Id == input.Id);
            var old = index < 0 ? null : s.Tasks[index];
            if (expectedVersion is not null && old?.UpdatedAtUtc != expectedVersion) throw new InvalidOperationException("任务已在其他窗口改变，请重新打开后编辑。");
            var now = clock.GetUtcNow();
            var updated = old is null || now > old.UpdatedAtUtc ? now : old.UpdatedAtUtc.AddTicks(1);
            var scheduleChanged = old is null || old.DeadlineUtc != input.DeadlineUtc || !old.ReminderRules.Order().SequenceEqual(input.ReminderRules.Order());
            var item = input with { Title = input.Title.Trim(), UpdatedAtUtc = updated, CreatedAtUtc = old?.CreatedAtUtc ?? now, Status = old?.Status ?? TaskStatus.Pending, ScheduleRevision = scheduleChanged ? (old?.ScheduleRevision ?? 0) + 1 : old!.ScheduleRevision };
            if (index < 0) s.Tasks.Add(item); else s.Tasks[index] = item;
            if (scheduleChanged) ReminderRules.ReplaceSchedule(s, item, now);
            return true;
        });
    }

    public async Task SetStatusAsync(string id, TaskStatus status, DateTimeOffset? expected = null)
    {
        DeadlineTask? before = null, after = null;
        await store.CommitAsync(s =>
        {
            var i = s.Tasks.FindIndex(t => t.Id == id);
            if (i < 0) throw new InvalidOperationException("这项任务已不存在。");
            var old = s.Tasks[i];
            if (expected is not null && old.UpdatedAtUtc != expected) throw new InvalidOperationException("任务已改变，请重试。");
            if (old.Status == status) return false;
            before = old;
            var now = clock.GetUtcNow();
            after = old with { Status = status, StatusBeforeDelete = status == TaskStatus.Deleted ? old.Status : old.StatusBeforeDelete, UpdatedAtUtc = now > old.UpdatedAtUtc ? now : old.UpdatedAtUtc.AddTicks(1), CompletedAtUtc = status == TaskStatus.Completed ? now : status == TaskStatus.Pending ? null : old.CompletedAtUtc, DeletedAtUtc = status == TaskStatus.Deleted ? now : null, ScheduleRevision = old.ScheduleRevision + 1 };
            s.Tasks[i] = after;
            ReminderRules.ReplaceSchedule(s, after, now);
            return true;
        });
        if (before is not null && after is not null) { undoOriginal = before; undoId = id; undoVersion = after.UpdatedAtUtc; }
    }

    public async Task UndoAsync()
    {
        var original = undoOriginal;
        var id = undoId;
        if (original is null || id is null) throw new InvalidOperationException("没有可以撤销的操作。");
        await store.CommitAsync(s =>
        {
            var i = s.Tasks.FindIndex(t => t.Id == id);
            if (i < 0 || s.Tasks[i].UpdatedAtUtc != undoVersion) throw new InvalidOperationException("任务已再次改变，这次操作无法撤销。");
            var now = clock.GetUtcNow();
            var restored = original with { UpdatedAtUtc = now > s.Tasks[i].UpdatedAtUtc ? now : s.Tasks[i].UpdatedAtUtc.AddTicks(1), ScheduleRevision = s.Tasks[i].ScheduleRevision + 1 };
            s.Tasks[i] = restored;
            ReminderRules.ReplaceSchedule(s, restored, clock.GetUtcNow());
            return true;
        });
        undoOriginal = null;
    }

    public static ExportDocument ParseImport(string json)
    {
        if (json.Length > 16 * 1024 * 1024) throw new InvalidDataException("导入文件不能超过 16 MB。");
        var doc = JsonSerializer.Deserialize<ExportDocument>(json, Serialization.Json) ?? throw new InvalidDataException("文件内容为空。");
        if (doc.SchemaVersion != 1 || doc.Tasks is null || doc.Tasks.Count > 10000) throw new InvalidDataException("文件版本或任务数量不受支持。");
        var ids = new HashSet<string>();
        for (var i = 0; i < doc.Tasks.Count; i++)
        {
            try { Validation.Task(doc.Tasks[i]); if (!ids.Add(doc.Tasks[i].Id)) throw new InvalidDataException("同一文件存在重复 ID。"); }
            catch (InvalidDataException e) { throw new InvalidDataException($"第 {i + 1} 项：{e.Message}"); }
        }
        if (doc.Settings is not null) Validation.Settings(doc.Settings);
        return doc;
    }

    public async Task ImportAsync(ExportDocument input, bool useNewer, bool includeSettings)
    {
        // Validate the entire payload before creating a backup or changing any state.
        _ = ParseImport(JsonSerializer.Serialize(input, Serialization.Json));
        await store.BackupAsync("import");
        await store.CommitAsync(s =>
        {
            foreach (var incoming in input.Tasks)
            {
                var i = s.Tasks.FindIndex(t => t.Id == incoming.Id);
                if (i >= 0 && (!useNewer || incoming.UpdatedAtUtc <= s.Tasks[i].UpdatedAtUtc)) continue;
                var item = incoming with { ScheduleRevision = Math.Max(incoming.ScheduleRevision, i < 0 ? 0 : s.Tasks[i].ScheduleRevision) + 1 };
                if (i < 0) s.Tasks.Add(item); else s.Tasks[i] = item;
                ReminderRules.ReplaceSchedule(s, item, clock.GetUtcNow());
            }
            if (includeSettings && input.Settings is not null) s.Settings = input.Settings with { AutoStartPreference = s.Settings.AutoStartPreference };
            return true;
        });
    }
}

public static class ReminderRules
{
    public static void ReplaceSchedule(AppState state, DeadlineTask task, DateTimeOffset now)
    {
        for (var i = 0; i < state.Reminders.Count; i++)
            if (state.Reminders[i].TaskId == task.Id && state.Reminders[i].DeliveryState is not DeliveryState.Processed and not DeliveryState.Cancelled and not DeliveryState.Superseded)
                state.Reminders[i] = state.Reminders[i] with { DeliveryState = DeliveryState.Cancelled };
        if (task.Status != TaskStatus.Pending) return;
        foreach (var seconds in task.ReminderRules.Distinct())
        {
            var at = task.DeadlineUtc.AddSeconds(-seconds);
            state.Reminders.Add(new ReminderEvent { Id = $"{task.Id}:{task.ScheduleRevision}:{seconds}", TaskId = task.Id, ScheduleRevision = task.ScheduleRevision, RuleId = seconds, ScheduledAtUtc = at, DeliveryState = at > now ? DeliveryState.Planned : DeliveryState.Superseded, Reason = at > now ? "" : "创建或修改时已过去，不补发" });
        }
    }

    public static ReminderEvent[] Due(AppState state, DateTimeOffset now) => state.Reminders.Where(e => e.DeliveryState is DeliveryState.Planned or DeliveryState.Snoozed || e.DeliveryState == DeliveryState.Failed && e.FailureCount < 3).Where(e => e.EffectiveTrigger <= now && state.Tasks.Any(t => t.Id == e.TaskId && t.Status == TaskStatus.Pending && t.ScheduleRevision == e.ScheduleRevision)).OrderBy(e => e.EffectiveTrigger).ToArray();
    public static ReminderEvent[] Relevant(IEnumerable<ReminderEvent> events) => events.GroupBy(e => e.TaskId).Select(g => g.OrderByDescending(e => e.EffectiveTrigger).First()).ToArray();
}
