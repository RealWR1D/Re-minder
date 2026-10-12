namespace ReMinder.Domain;

public sealed record TimePresentation(string Absolute, string Compact, string Detailed, string Urgency);

public static class DeadlineClock
{
    public static TimePresentation Format(DateTimeOffset deadline, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var date = TimeZoneInfo.ConvertTime(deadline, zone);
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var relative = (date.Date - localNow.Date).Days;
        var prefix = relative switch { 0 => "今天", 1 => "明天", -1 => "昨天", _ => date.Year == localNow.Year ? $"{date.Month}月{date.Day}日" : $"{date.Year}年{date.Month}月{date.Day}日" };
        var absolute = $"{prefix} {date:HH:mm}";
        var left = deadline - now;
        if (left < TimeSpan.Zero)
        {
            var ago = -left;
            var value = ago.TotalMinutes < 1 ? "刚刚逾期" : ago.TotalHours < 1 ? $"逾期 {(int)ago.TotalMinutes} 分钟" : ago.TotalDays < 1 ? $"逾期 {(int)ago.TotalHours} 小时" : $"逾期 {(int)ago.TotalDays} 天";
            return new(absolute, value, value, "error");
        }
        if (left == TimeSpan.Zero) return new(absolute, "已到截止时间", "已到截止时间", "warning");
        var minutes = (int)Math.Ceiling(left.TotalMinutes);
        var hours = (int)left.TotalHours;
        var days = (int)left.TotalDays;
        var compact = left.TotalMinutes < 1 ? "不到 1 分钟" : left.TotalHours < 1 ? $"剩 {minutes} 分钟" : left.TotalHours < 48 ? $"剩 {hours} 小时" : $"剩 {days} 天";
        var detailed = left.TotalMinutes < 1 ? "不到 1 分钟" : left.TotalHours < 1 ? $"还剩 {minutes} 分钟" : left.TotalHours < 48 ? $"还剩 {hours} 小时 {left.Minutes} 分" : $"还剩 {days} 天 {left.Hours} 小时";
        return new(absolute, compact, detailed, left.TotalHours <= 24 ? "warning" : "normal");
    }

    public static DeadlineTask[] Pending(IEnumerable<DeadlineTask> tasks) => tasks.Where(t => t.Status == TaskStatus.Pending).OrderBy(t => t.DeadlineUtc).ThenBy(t => t.CreatedAtUtc).ThenBy(t => t.Id, StringComparer.Ordinal).ToArray();
    public static DeadlineTask? Next(IEnumerable<DeadlineTask> tasks, DateTimeOffset now)
    {
        var pending = Pending(tasks);
        return pending.FirstOrDefault(t => t.DeadlineUtc >= now) ?? pending.FirstOrDefault();
    }

    public static DateTimeOffset ToUtc(DateTime date, TimeSpan time, TimeZoneInfo zone, TimeSpan? ambiguousOffset = null)
    {
        var local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) throw new ArgumentException("这个时间因夏令时调整不存在，请重新选择。");
        if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            if (ambiguousOffset is null || !offsets.Contains(ambiguousOffset.Value)) throw new ArgumentException("这个时间出现两次，请选择 UTC 偏移。");
            return new DateTimeOffset(local, ambiguousOffset.Value).ToUniversalTime();
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
