using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;

namespace ReMinder.App.Services;

internal sealed class NotificationService : IDisposable
{
    private bool registered;
    public bool Available => registered;
    public string RegistrationError { get; private set; } = "";
    public event Action<Dictionary<string, string>>? Invoked;
    public void Register()
    {
        try
        {
            if (!AppNotificationManager.IsSupported()) { RegistrationError = "当前环境不支持系统通知。"; return; }
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            AppNotificationManager.Default.Register("Re:minder", new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "re-minder.png")));
            registered = true;
        }
        catch (Exception e) when (e is COMException or TypeInitializationException or DllNotFoundException or InvalidOperationException)
        { RegistrationError = "系统通知初始化失败。提醒会保留在待处理列表中。"; Log.Write("notification-register", e); }
    }
    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => Handle(args);
    public void Handle(AppNotificationActivatedEventArgs args) => Invoked?.Invoke(args.Arguments.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
    public string Status
    {
        get
        {
            if (!registered) return RegistrationError;
            try { return AppNotificationManager.Default.Setting switch { AppNotificationSetting.Enabled => "系统通知已启用；横幅与声音遵循 Windows 设置。", AppNotificationSetting.DisabledForApplication => "此应用的通知已被关闭。", AppNotificationSetting.DisabledForUser => "Windows 已关闭全局通知。", AppNotificationSetting.DisabledByGroupPolicy => "通知被组织策略关闭。", _ => "通知状态不可用，请检查 Windows 通知设置。" }; }
            catch (COMException) { return "暂时无法读取通知状态。"; }
        }
    }
    public bool CanSend { get { try { return registered && AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled; } catch (COMException) { return false; } } }
    public static string Tag(IEnumerable<string> ids) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", ids.Order()))))[..16];
    public uint Send(AppState snapshot, ReminderEvent[] events, string tag)
    {
        var builder = new AppNotificationBuilder();
        if (events.Length > 1)
        {
            builder.AddText($"有 {events.Length} 项任务需要留意").AddText("打开 Re:minder 查看截止时间与待处理提醒。").AddArgument("action", "reminders");
            builder.AddButton(new AppNotificationButton("查看提醒").AddArgument("action", "reminders"));
        }
        else
        {
            var e = events[0]; var t = snapshot.Tasks.First(x => x.Id == e.TaskId); var view = DeadlineClock.Format(t.DeadlineUtc, DateTimeOffset.UtcNow);
            builder.AddText(t.Title).AddText($"{view.Absolute} · {view.Compact}");
            Add(builder, "open", e);
            foreach (var (label, action) in new[] { ("打开任务", "open"), ("稍后 10 分钟", "snooze10"), ("完成", "complete") })
            {
                var b = new AppNotificationButton(label).AddArgument("action", action).AddArgument("taskId", e.TaskId).AddArgument("eventId", e.Id).AddArgument("revision", e.ScheduleRevision.ToString()).AddArgument("attempt", e.Attempts.ToString());
                builder.AddButton(b);
            }
        }
        if (!snapshot.Settings.SoundEnabled) builder.MuteAudio();
        var notification = builder.BuildNotification(); notification.Tag = tag; notification.Group = "reminders";
        AppNotificationManager.Default.Show(notification);
        if (notification.Id == 0) throw new InvalidOperationException("系统未接受这条通知。");
        return notification.Id;
    }
    private static void Add(AppNotificationBuilder b, string action, ReminderEvent e) => b.AddArgument("action", action).AddArgument("taskId", e.TaskId).AddArgument("eventId", e.Id).AddArgument("revision", e.ScheduleRevision.ToString()).AddArgument("attempt", e.Attempts.ToString());
    public void Test()
    {
        if (!CanSend) throw new InvalidOperationException(Status);
        var n = new AppNotificationBuilder().AddText("Re:minder 已准备好").AddText("这是一条测试提醒。真实提醒会包含任务名称和截止时间。").AddArgument("action", "settings").AddButton(new AppNotificationButton("打开设置").AddArgument("action", "settings")).MuteAudio().BuildNotification();
        n.Tag = "system-test"; n.Group = "test"; AppNotificationManager.Default.Show(n);
        if (n.Id == 0) throw new InvalidOperationException("系统未接受测试通知。");
    }
    public async Task RemoveAsync(string tag)
    {
        if (!registered || string.IsNullOrWhiteSpace(tag)) return;
        try { await AppNotificationManager.Default.RemoveByTagAndGroupAsync(tag, "reminders"); }
        catch (COMException e) { Log.Write("notification-remove", e); }
    }
    public void Dispose() { if (registered) { AppNotificationManager.Default.NotificationInvoked -= OnInvoked; AppNotificationManager.Default.Unregister(); } }
    public static void UninstallRegistration() => AppNotificationManager.Default.UnregisterAll();
}

internal static class Log
{
    public static string DirectoryPath { get; set; } = "";
    public static void Write(string type, Exception? error = null)
    {
        if (string.IsNullOrEmpty(DirectoryPath)) return;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, "diagnostics.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O}\t{type}\t{error?.GetType().Name}\t{error?.HResult:X8}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
