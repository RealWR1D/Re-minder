using ReMinder.Domain;
using System.Text.Json;
using TaskStatus = ReMinder.Domain.TaskStatus;

if (args.FirstOrDefault() == "--seed-qa")
{
    var qaDirectory = Path.GetFullPath(args[1]);
    if (!qaDirectory.EndsWith(Path.Combine("artifacts", "qa-data"), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only the dedicated artifacts/qa-data directory may be seeded.");
    var qaStore = new StateStore(qaDirectory); await qaStore.LoadAsync(); var qaService = new TaskService(qaStore);
    var existing = qaStore.Snapshot.Tasks.FirstOrDefault(t => t.Id == "qa-system-reminder");
    if (existing is not null && existing.Status != TaskStatus.Pending) { await qaService.SetStatusAsync(existing.Id, TaskStatus.Pending); existing = qaStore.Snapshot.Tasks.First(t => t.Id == existing.Id); }
    var utc = DateTimeOffset.UtcNow;
    await qaService.SaveAsync((existing ?? new DeadlineTask { Id = "qa-system-reminder", CreatedAtUtc = utc, UpdatedAtUtc = utc }) with { Title = "系统提醒验收 · 本机测试", DeadlineUtc = utc.AddSeconds(30), ReminderRules = [0], InputLocalDateTime = utc.AddSeconds(30).LocalDateTime.ToString("yyyy-MM-ddTHH:mm:ss") }, existing?.UpdatedAtUtc);
    Console.WriteLine("QA reminder scheduled 30 seconds from now."); return;
}

var root = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine("artifacts", "test-data", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root); var results = new List<object>(); var count = 0;
async Task Check(string name, Func<Task> work) { await work(); ++count; results.Add(new { name, passed = true }); Console.WriteLine($"PASS {name}"); }
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task Throws<T>(Func<Task> work) where T : Exception { try { await work(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
var now = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
DeadlineTask Item(string title, int minutes = 60, string? id = null) => new() { Id = id ?? Guid.NewGuid().ToString("N"), Title = title, DeadlineUtc = now.AddMinutes(minutes), CreatedAtUtc = now, UpdatedAtUtc = now, InputLocalDateTime = "2026-10-12T16:00:00" };

await Check("倒计时边界与稳定排序", () =>
{
    Assert(DeadlineClock.Format(now.AddSeconds(1), now).Compact == "不到 1 分钟", "subminute"); Assert(DeadlineClock.Format(now, now).Compact == "已到截止时间", "exact"); Assert(DeadlineClock.Format(now.AddSeconds(-1), now).Compact == "刚刚逾期", "overdue"); Assert(DeadlineClock.Format(now.AddMinutes(59.1), now).Compact == "剩 60 分钟", "ceil");
    var tasks = new[] { Item("future", 10, "b"), Item("overdue", -2, "a"), Item("same", 10, "c"), Item("done", -3) with { Status = TaskStatus.Completed } };
    Assert(DeadlineClock.Pending(tasks).Select(t => t.Id).SequenceEqual(new[] { "a", "b", "c" }), "order"); Assert(DeadlineClock.Next(tasks, now)?.Id == "b", "future summary"); Assert(DeadlineClock.Next(tasks, now.AddHours(2))?.Id == "a", "all overdue"); return Task.CompletedTask;
});
await Check("夏令时不存在与重复时刻", async () =>
{
    var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
    await Throws<ArgumentException>(() => Task.FromResult(DeadlineClock.ToUtc(new DateTime(2026, 3, 8), TimeSpan.FromHours(2.5), zone)));
    await Throws<ArgumentException>(() => Task.FromResult(DeadlineClock.ToUtc(new DateTime(2026, 11, 1), TimeSpan.FromHours(1.5), zone)));
    var a = DeadlineClock.ToUtc(new DateTime(2026, 11, 1), TimeSpan.FromHours(1.5), zone, TimeSpan.FromHours(-7)); var b = DeadlineClock.ToUtc(new DateTime(2026, 11, 1), TimeSpan.FromHours(1.5), zone, TimeSpan.FromHours(-8)); Assert((b - a).TotalHours == 1, "distinct UTC");
});
var store = new StateStore(Path.Combine(root, "main")); await store.LoadAsync(); var clock = new TestClock(now); var service = new TaskService(store, clock); var task = Item("首次任务", 120);
await Check("空启动、保存与未来提醒", async () => { Assert(store.Snapshot.Tasks.Count == 0, "empty"); await service.SaveAsync(task, null); Assert(store.Snapshot.Reminders.Count == 2 && store.Snapshot.Reminders.All(e => e.DeliveryState == DeliveryState.Planned), "future plan"); var reload = new StateStore(store.DirectoryPath); await reload.LoadAsync(); Assert(reload.Snapshot.Tasks.Single().Title == "首次任务", "persist"); });
await Check("创建过去任务不补发", async () => { await service.SaveAsync(Item("已过去", -1), null); Assert(ReminderRules.Due(store.Snapshot, now).Length == 0, "no past spam"); });
await Check("修改标题保留账本、改期作废旧提醒", async () =>
{
    var old = store.Snapshot.Tasks.First(t => t.Id == task.Id); clock.Advance(); await service.SaveAsync(old with { Title = "改名" }, old.UpdatedAtUtc); Assert(store.Snapshot.Tasks.First(t => t.Id == task.Id).ScheduleRevision == old.ScheduleRevision, "title no revision");
    old = store.Snapshot.Tasks.First(t => t.Id == task.Id); clock.Advance(); await service.SaveAsync(old with { DeadlineUtc = now.AddHours(4) }, old.UpdatedAtUtc); Assert(store.Snapshot.Reminders.Count(e => e.TaskId == task.Id && e.DeliveryState == DeliveryState.Cancelled) == 2, "old cancellation");
    await Throws<InvalidOperationException>(() => service.SaveAsync(old with { Title = "旧版本写入" }, old.UpdatedAtUtc));
});
await Check("完成、撤销、软删除与恢复", async () =>
{
    clock.Advance(); await service.SetStatusAsync(task.Id, TaskStatus.Completed); Assert(!store.Snapshot.Reminders.Any(e => e.TaskId == task.Id && e.DeliveryState == DeliveryState.Planned), "complete cancel"); clock.Advance(); await service.UndoAsync(); Assert(store.Snapshot.Tasks.First(t => t.Id == task.Id).Status == TaskStatus.Pending, "undo");
    clock.Advance(); await service.SetStatusAsync(task.Id, TaskStatus.Deleted); var deleted = store.Snapshot.Tasks.First(t => t.Id == task.Id); Assert(deleted.DeletedAtUtc is not null && deleted.StatusBeforeDelete == TaskStatus.Pending, "soft delete"); clock.Advance(); await service.SetStatusAsync(task.Id, deleted.StatusBeforeDelete!.Value); Assert(store.Snapshot.Tasks.First(t => t.Id == task.Id).DeletedAtUtc is null, "restore");
});
await Check("撤销不覆盖并发修改", async () => { clock.Advance(); await service.SetStatusAsync(task.Id, TaskStatus.Completed); var old = store.Snapshot.Tasks.First(t => t.Id == task.Id); clock.Advance(); await service.SaveAsync(old with { Title = "新的修改" }, old.UpdatedAtUtc); await Throws<InvalidOperationException>(service.UndoAsync); Assert(store.Snapshot.Tasks.First(t => t.Id == task.Id).Title == "新的修改", "preserve"); clock.Advance(); await service.SetStatusAsync(task.Id, TaskStatus.Pending); });
await Check("恢复提醒按每项任务选择最新规则", () => { var due = ReminderRules.Due(store.Snapshot, now.AddDays(1)); Assert(due.Length == 2, "both future rules"); Assert(ReminderRules.Relevant(due).Length == 1 && ReminderRules.Relevant(due)[0].RuleId == 0, "latest single"); return Task.CompletedTask; });
await Check("导入先全量校验且失败不改变数据", async () =>
{
    var version = store.Snapshot.StateVersion; var bad = new ExportDocument { Tasks = [Item("good"), Item("")] }; await Throws<InvalidDataException>(() => service.ImportAsync(bad, true, false)); Assert(store.Snapshot.StateVersion == version, "atomic bad import");
    var duplicate = new ExportDocument { Tasks = [Item("a", id: "dupe"), Item("b", id: "dupe")] }; await Throws<InvalidDataException>(() => Task.FromResult(TaskService.ParseImport(JsonSerializer.Serialize(duplicate, Serialization.Json))));
    var merged = new ExportDocument { Tasks = [Item("新增", id: "newtask"), store.Snapshot.Tasks.First(t => t.Id == task.Id) with { UpdatedAtUtc = now.AddDays(2), Title = "冲突较新版" }] }; await service.ImportAsync(merged, true, false); Assert(store.Snapshot.Tasks.Any(t => t.Id == "newtask") && store.Snapshot.Tasks.First(t => t.Id == task.Id).Title == "冲突较新版", "merge");
});
await Check("保存失败保留内存与原文件", async () => { var snapshot = store.Snapshot; var text = await File.ReadAllTextAsync(store.DataPath); using (var locked = new FileStream(store.DataPath, FileMode.Open, FileAccess.Read, FileShare.None)) await Throws<IOException>(() => store.CommitAsync(s => { s.Tasks.Add(Item("不可写")); return true; })); Assert(store.Snapshot.StateVersion == snapshot.StateVersion && await File.ReadAllTextAsync(store.DataPath) == text, "no divergence"); });
await Check("备份最多七份与损坏恢复", async () => { for (var i = 0; i < 10; i++) await store.BackupAsync("check"); Assert(Directory.GetFiles(store.BackupPath, "*.json").Length == 7, "backup limit"); await File.WriteAllTextAsync(store.DataPath, "{broken"); var recovered = new StateStore(store.DirectoryPath); await recovered.LoadAsync(); Assert(!recovered.ReadOnly && recovered.Snapshot.Tasks.Any(t => t.Id == task.Id), "recovered"); Assert(Directory.GetFiles(store.DirectoryPath, "damaged-*.json").Length == 1, "preserve damaged"); });
await Check("未来版本只读不覆盖", async () => { var future = new StateStore(Path.Combine(root, "future")); Directory.CreateDirectory(future.DirectoryPath); const string text = "{\"schemaVersion\":99}"; await File.WriteAllTextAsync(future.DataPath, text); await future.LoadAsync(); Assert(future.ReadOnly, "read only"); await Throws<InvalidOperationException>(() => future.CommitAsync(s => true)); Assert(await File.ReadAllTextAsync(future.DataPath) == text, "preserved"); });
await Check("IPC 帧与协议校验", async () => { using var stream = new MemoryStream(); await Wire.WriteAsync(stream, new WireMessage { Kind = "snapshot", State = store.Snapshot }, new SemaphoreSlim(1)); stream.Position = 0; var message = await Wire.ReadAsync(stream); Assert(message.State?.StateVersion == store.Snapshot.StateVersion, "roundtrip"); using var invalid = new MemoryStream(new byte[] { 255, 255, 255, 127 }); await Throws<InvalidDataException>(() => Wire.ReadAsync(invalid)); });
await Check("系统时钟倒退仍拒绝旧版本覆盖", async () => { var fixedClock = new TestClock(now); var isolated = new StateStore(Path.Combine(root, "monotonic")); await isolated.LoadAsync(); var changes = new TaskService(isolated, fixedClock); var item = Item("时钟测试"); await changes.SaveAsync(item, null); var old = isolated.Snapshot.Tasks.Single(); await changes.SaveAsync(old with { Title = "修改后" }, old.UpdatedAtUtc); Assert(isolated.Snapshot.Tasks.Single().UpdatedAtUtc > old.UpdatedAtUtc, "monotonic update"); await Throws<InvalidOperationException>(() => changes.SaveAsync(old, old.UpdatedAtUtc)); });

Directory.CreateDirectory(Path.Combine(root, "..")); await File.WriteAllTextAsync(Path.Combine(root, "..", "checks.json"), JsonSerializer.Serialize(new { passed = count, failed = 0, utc = DateTimeOffset.UtcNow, results }, Serialization.Json)); Console.WriteLine($"{count} checks passed");
sealed class TestClock(DateTimeOffset initial) : TimeProvider { private DateTimeOffset now = initial; public override DateTimeOffset GetUtcNow() => now; public void Advance() => now = now.AddSeconds(1); }
