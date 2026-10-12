using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReMinder.Domain;

public enum TaskStatus { Pending, Completed, Deleted }
public enum DeliveryState { Planned, Prepared, Accepted, Snoozed, Processed, Cancelled, Superseded, Blocked, Failed, Unknown }

public sealed record DeadlineTask
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; init; } = "";
    public string Notes { get; init; } = "";
    public DateTimeOffset DeadlineUtc { get; init; }
    public string InputTimeZoneId { get; init; } = TimeZoneInfo.Local.Id;
    public string InputLocalDateTime { get; init; } = "";
    public TaskStatus Status { get; init; }
    public TaskStatus? StatusBeforeDelete { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public DateTimeOffset? DeletedAtUtc { get; init; }
    public int ScheduleRevision { get; init; } = 1;
    public int[] ReminderRules { get; init; } = [3600, 0];
}

public sealed record ReminderEvent
{
    public string Id { get; init; } = "";
    public string TaskId { get; init; } = "";
    public int ScheduleRevision { get; init; }
    public int RuleId { get; init; }
    public DateTimeOffset ScheduledAtUtc { get; init; }
    public DateTimeOffset? SnoozedUntilUtc { get; init; }
    public DeliveryState DeliveryState { get; init; }
    public string? AttemptId { get; init; }
    public DateTimeOffset? LastAttemptAtUtc { get; init; }
    public uint NotificationId { get; init; }
    public int Attempts { get; init; }
    public int FailureCount { get; init; }
    public DateTimeOffset? RetryAtUtc { get; init; }
    public string Tag { get; init; } = "";
    public int SnoozeCount { get; init; }
    public int LastSnoozedAttempt { get; init; } = -1;
    public string Reason { get; init; } = "";
    public DateTimeOffset EffectiveTrigger => DeliveryState == DeliveryState.Failed ? RetryAtUtc ?? ScheduledAtUtc : SnoozedUntilUtc ?? ScheduledAtUtc;
}

public sealed record AppSettings
{
    public string ThemeMode { get; init; } = "system";
    public string PaletteId { get; init; } = "purple";
    public int PanelWidthDip { get; init; } = 360;
    public string MonitorPreference { get; init; } = "primary";
    public int TopMarginDip { get; init; } = 24;
    public int RightMarginDip { get; init; } = 24;
    public string WindowMode { get; init; } = "desktop";
    public bool Collapsed { get; init; }
    public bool PanelVisible { get; init; } = true;
    public bool ReduceMotion { get; init; }
    public int[] DefaultReminderRules { get; init; } = [3600, 0];
    public bool SoundEnabled { get; init; }
    public bool AutoStartPreference { get; init; }
    public string LastTimeZoneId { get; init; } = TimeZoneInfo.Local.Id;
}

public sealed record AppState
{
    public int SchemaVersion { get; init; } = 1;
    public long StateVersion { get; init; }
    public List<DeadlineTask> Tasks { get; init; } = [];
    public List<ReminderEvent> Reminders { get; init; } = [];
    public AppSettings Settings { get; set; } = new();
}

public sealed record ExportDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string Application { get; init; } = "Re:minder";
    public List<DeadlineTask> Tasks { get; init; } = [];
    public AppSettings? Settings { get; init; }
}

public static class Serialization
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 32
    };
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
}

public static class Validation
{
    public static void Task(DeadlineTask t)
    {
        if (string.IsNullOrWhiteSpace(t.Id) || t.Id.Length > 128 || t.Id.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_'))) throw new InvalidDataException("任务 ID 无效。");
        if (string.IsNullOrWhiteSpace(t.Title) || t.Title.Trim().Length > 120) throw new InvalidDataException("任务名称须为 1–120 个字符。");
        if (t.Notes is null || t.Notes.Length > 2000) throw new InvalidDataException("备注不能超过 2000 个字符。");
        if (t.DeadlineUtc == default || t.CreatedAtUtc == default || t.UpdatedAtUtc == default || t.ScheduleRevision < 1) throw new InvalidDataException("任务日期或版本无效。");
        if (!Enum.IsDefined(t.Status) || (t.StatusBeforeDelete is not null && !Enum.IsDefined(t.StatusBeforeDelete.Value))) throw new InvalidDataException("任务状态无效。");
        if (t.ReminderRules is null || t.ReminderRules.Length > 8 || t.ReminderRules.Any(x => x < 0 || x > 31536000)) throw new InvalidDataException("提醒偏移须在 0 秒至 365 天之间，最多 8 条。");
        if (t.InputTimeZoneId is null || t.InputTimeZoneId.Length > 256 || t.InputLocalDateTime is null || t.InputLocalDateTime.Length > 64) throw new InvalidDataException("时区信息无效。");
    }

    public static void Settings(AppSettings s)
    {
        if (s is null || !new[] { "system", "light", "dark" }.Contains(s.ThemeMode) || !new[] { "purple", "blue", "teal", "rose" }.Contains(s.PaletteId)) throw new InvalidDataException("外观设置无效。");
        if (!new[] { 320, 360, 400 }.Contains(s.PanelWidthDip) || s.TopMarginDip is < 0 or > 1000 || s.RightMarginDip is < 0 or > 1000 || s.MonitorPreference is null || s.MonitorPreference.Length > 256) throw new InvalidDataException("面板位置设置无效。");
        if (!new[] { "desktop", "topmost" }.Contains(s.WindowMode) || s.DefaultReminderRules is null || s.DefaultReminderRules.Length > 8 || s.DefaultReminderRules.Any(x => x < 0 || x > 31536000)) throw new InvalidDataException("提醒或窗口模式设置无效。");
    }

    public static void State(AppState state)
    {
        if (state.SchemaVersion != 1) throw new InvalidDataException("数据版本不兼容，原文件未被覆盖。");
        if (state.Tasks is null || state.Reminders is null || state.Tasks.Count > 100000 || state.Reminders.Count > 800000) throw new InvalidDataException("数据内容或数量无效。");
        var ids = new HashSet<string>();
        foreach (var t in state.Tasks) { Task(t); if (!ids.Add(t.Id)) throw new InvalidDataException("存在重复任务 ID。"); }
        Settings(state.Settings);
        var eventIds = new HashSet<string>();
        foreach (var e in state.Reminders)
        {
            if (string.IsNullOrWhiteSpace(e.Id) || e.Id.Length > 256 || !eventIds.Add(e.Id) || !ids.Contains(e.TaskId) || e.ScheduledAtUtc == default || !Enum.IsDefined(e.DeliveryState) || e.Attempts < 0 || e.SnoozeCount < 0 || e.RuleId < 0 || e.ScheduleRevision < 1) throw new InvalidDataException("提醒账本无效。");
        }
    }
}
