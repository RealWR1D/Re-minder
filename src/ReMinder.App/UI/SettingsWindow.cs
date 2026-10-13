using ReMinder.App.Platform;
using ReMinder.App.Services;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;

namespace ReMinder.App.UI;

internal sealed class SettingsWindow : FrameWindow
{
    private readonly StateStore store; private readonly TaskService tasks; private readonly NotificationService notifications;
    private readonly ComboBox theme, palette, width, mode, monitor;
    private readonly TextBox top = new(), right = new();
    private readonly CheckBox sound = new() { Content = "提醒声音（同时遵循 Windows 设置）" }, reduced = new() { Content = "减少动态效果" }, auto = new() { Content = "登录 Windows 时启动" };
    private readonly CheckBox hour = new() { Content = "默认提前 1 小时提醒" }, deadline = new() { Content = "默认截止时提醒" };
    private readonly TextBlock status = Controls.Text("", "Small"), error = Controls.Text("");
    private readonly TextBlock hostLabel = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly AppSettings original;
    private bool saving;
    public SettingsWindow(StateStore store, TaskService tasks, NotificationService notifications, string hostStatus) : base("设置", 460)
    {
        this.store = store; this.tasks = tasks; this.notifications = notifications; original = store.Snapshot.Settings;
        theme = Choice(new[] { "跟随系统", "浅色", "深色" }, Array.IndexOf(new[] { "system", "light", "dark" }, original.ThemeMode));
        palette = Choice(new[] { "柔和紫", "雾蓝", "青绿", "玫瑰" }, Array.IndexOf(new[] { "purple", "blue", "teal", "rose" }, original.PaletteId));
        width = Choice(new[] { "紧凑 · 320", "标准 · 360", "宽松 · 400" }, Array.IndexOf(new[] { 320, 360, 400 }, original.PanelWidthDip));
        mode = Choice(new[] { "固定在桌面", "始终置顶" }, original.WindowMode == "desktop" ? 0 : 1);
        var screens = System.Windows.Forms.Screen.AllScreens.Select(s => s.DeviceName).ToArray(); var monitors = new[] { "主显示器" }.Concat(screens).ToArray(); monitor = Choice(monitors, Array.IndexOf(screens, original.MonitorPreference) + 1);
        top.Text = original.TopMarginDip.ToString(); right.Text = original.RightMarginDip.ToString(); sound.IsChecked = original.SoundEnabled; reduced.IsChecked = original.ReduceMotion; auto.IsChecked = AutoStartService.Enabled;
        hour.IsChecked = original.DefaultReminderRules.Contains(3600); deadline.IsChecked = original.DefaultReminderRules.Contains(0);
        Add("外观", theme); Add("主题颜色", palette); Add("面板宽度", width); Body.Children.Add(reduced);
        Add("桌面与窗口", mode); hostLabel.Text = hostStatus; Body.Children.Add(hostLabel); Add("显示器", monitor);
        var margins = new Grid(); margins.ColumnDefinitions.Add(new ColumnDefinition()); margins.ColumnDefinitions.Add(new ColumnDefinition()); var a = new StackPanel(); a.Children.Add(Controls.Label("顶部间距（DIP）")); a.Children.Add(top); margins.Children.Add(a); var b = new StackPanel { Margin = new Thickness(12, 0, 0, 0) }; b.Children.Add(Controls.Label("右侧间距（DIP）")); b.Children.Add(right); Grid.SetColumn(b, 1); margins.Children.Add(b); Body.Children.Add(margins);
        Add("系统通知", status); status.Text = notifications.Status;
        var notificationButtons = new WrapPanel(); notificationButtons.Children.Add(Controls.Button("发送测试通知", () => Controls.Safely(() => { notifications.Test(); status.Text = "测试通知已被系统接受，请在通知中心查看。"; return Task.CompletedTask; }, ShowError), "SecondaryButton", "bell")); notificationButtons.Children.Add(Controls.Button("Windows 通知设置", () => Open("ms-settings:notifications"))); Body.Children.Add(notificationButtons);
        Body.Children.Add(sound); Body.Children.Add(hour); Body.Children.Add(deadline); Body.Children.Add(Controls.Label("启动")); Body.Children.Add(auto);
        Add("数据与备份", new TextBlock { Text = "任务保存在本机，不上传。每次替换前保留备份，最多七份。最近删除保留 30 天。", TextWrapping = TextWrapping.Wrap });
        var data = new WrapPanel(); data.Children.Add(Controls.Button("导出", Export, "SecondaryButton", "export")); data.Children.Add(Controls.Button("导入", () => Controls.Safely(Import, ShowError), null, "import")); data.Children.Add(Controls.Button("打开数据目录", () => Open(store.DirectoryPath))); Body.Children.Add(data);
        if (!string.IsNullOrEmpty(store.RecoveryNotice)) Body.Children.Add(new TextBlock { Text = store.RecoveryNotice, TextWrapping = TextWrapping.Wrap });
        error.SetResourceReference(TextBlock.ForegroundProperty, "Error"); error.TextWrapping = TextWrapping.Wrap; Body.Children.Add(error);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) }; footer.Children.Add(Controls.Button("关闭", Close)); footer.Children.Add(Controls.Button("保存设置", () => Controls.Safely(Save, ShowError), "PrimaryButton", "check")); Body.Children.Add(footer);
        Body.Children.Add(new TextBlock { Text = $"Re:minder {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)} · Windows 桌面版\n退出后不再检查提醒；睡眠期间的提醒会在恢复时合并处理。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0), FontSize = 12 });
    }
    private static ComboBox Choice(string[] values, int selected) => new() { ItemsSource = values, SelectedIndex = Math.Max(0, selected), MinHeight = 40 };
    public void UpdateHostStatus(string text) => hostLabel.Text = text;
    private void Add(string label, UIElement content) { Body.Children.Add(Controls.Label(label)); Body.Children.Add(content); if (content is TextBox or DatePicker or ComboBox) System.Windows.Automation.AutomationProperties.SetName(content, label); }
    private void ShowError(string text) => error.Text = text;
    private static void Open(string target) => Controls.Safely(() => { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); return Task.CompletedTask; });
    private async Task Save()
    {
        if (saving) return;
        if (!int.TryParse(top.Text, out var topValue) || !int.TryParse(right.Text, out var rightValue)) throw new ArgumentException("间距需要填写整数。");
        var rules = original.DefaultReminderRules.Where(x => x is not 0 and not 3600).ToList(); if (hour.IsChecked == true) rules.Add(3600); if (deadline.IsChecked == true) rules.Add(0);
        var settings = original with { ThemeMode = new[] { "system", "light", "dark" }[theme.SelectedIndex], PaletteId = new[] { "purple", "blue", "teal", "rose" }[palette.SelectedIndex], PanelWidthDip = new[] { 320, 360, 400 }[width.SelectedIndex], WindowMode = mode.SelectedIndex == 0 ? "desktop" : "topmost", MonitorPreference = monitor.SelectedIndex == 0 ? "primary" : (string)monitor.SelectedItem, TopMarginDip = topValue, RightMarginDip = rightValue, SoundEnabled = sound.IsChecked == true, ReduceMotion = reduced.IsChecked == true, AutoStartPreference = auto.IsChecked == true, DefaultReminderRules = rules.Distinct().ToArray() };
        Validation.Settings(settings); var priorAuto = AutoStartService.Enabled; saving = true;
        try
        {
            if (settings.AutoStartPreference != priorAuto) AutoStartService.Set(settings.AutoStartPreference);
            try { await store.CommitAsync(s => { s.Settings = settings with { PanelVisible = s.Settings.PanelVisible, Collapsed = s.Settings.Collapsed }; return true; }); }
            catch { if (settings.AutoStartPreference != priorAuto) AutoStartService.Set(priorAuto); throw; }
            Close();
        }
        finally { saving = false; }
    }
    private void Export() => Controls.Safely(async () =>
    {
        var dialog = new SaveFileDialog { Title = "导出 Re:minder 数据", Filter = "JSON 文件|*.json", FileName = $"Re-minder-{DateTime.Now:yyyyMMdd}.json" };
        if (dialog.ShowDialog(this) != true) return;
        var s = store.Snapshot; await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(new ExportDocument { Tasks = s.Tasks, Settings = s.Settings }, Serialization.Json)); error.Text = "数据已导出。";
    }, ShowError);
    private async Task Import()
    {
        var dialog = new OpenFileDialog { Title = "导入 Re:minder 数据", Filter = "JSON 文件|*.json" }; if (dialog.ShowDialog(this) != true) return;
        if (new FileInfo(dialog.FileName).Length > 16 * 1024 * 1024) throw new InvalidDataException("导入文件不能超过 16 MB。");
        var document = TaskService.ParseImport(await File.ReadAllTextAsync(dialog.FileName)); var preview = new ImportWindow(document, store.Snapshot); preview.Owner = this; PlacementService.Input(preview, store.Snapshot.Settings);
        if (preview.ShowDialog() == true) { await tasks.ImportAsync(document, preview.UseNewer, preview.IncludeSettings); error.Text = "导入完成，原数据已备份。"; }
    }
}

internal sealed class ImportWindow : FrameWindow
{
    private readonly CheckBox newer = new() { Content = "相同 ID 使用更新时间较新的任务", IsChecked = true };
    private readonly CheckBox settings = new() { Content = "同时导入外观与提醒设置" };
    public bool UseNewer => newer.IsChecked == true;
    public bool IncludeSettings => settings.IsChecked == true;
    public ImportWindow(ExportDocument input, AppState state) : base("确认导入")
    {
        var added = input.Tasks.Count(t => !state.Tasks.Any(x => x.Id == t.Id)); var updates = input.Tasks.Count(t => state.Tasks.Any(x => x.Id == t.Id && t.UpdatedAtUtc > x.UpdatedAtUtc));
        Body.Children.Add(Controls.Text($"{input.Tasks.Count} 项任务已通过校验", "Title")); Body.Children.Add(Controls.Text($"新增 {added} 项 · 较新冲突 {updates} 项", "Small")); Body.Children.Add(new TextBlock { Text = "导入前会保存一份备份。已经过去的提醒不会补发。自启动设置保留本机选择。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        Body.Children.Add(newer); settings.IsEnabled = input.Settings is not null; Body.Children.Add(settings);
        var list = new StackPanel(); foreach (var task in input.Tasks.Where(t => state.Tasks.Any(x => x.Id == t.Id)).Take(20)) list.Children.Add(new TextBlock { Text = $"同 ID：{task.Title}", TextWrapping = TextWrapping.Wrap }); Body.Children.Add(list);
        Body.Children.Add(Controls.Button("确认导入", () => DialogResult = true, "PrimaryButton", "import"));
    }
}
