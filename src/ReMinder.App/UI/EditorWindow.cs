using System.Globalization;
using System.Windows.Input;

namespace ReMinder.App.UI;

internal sealed class EditorWindow : FrameWindow
{
    private readonly TextBox title = new() { MaxLength = 120 };
    private readonly TextBox notes = new() { MaxLength = 2000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 78, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly DatePicker date = new();
    private readonly TextBox time = new() { Width = 88, MaxLength = 5 };
    private readonly CheckBox hour = new() { Content = "提前 1 小时" };
    private readonly CheckBox at = new() { Content = "截止时" };
    private readonly TextBox custom = new() { ToolTip = "其他提前分钟数，用逗号分隔，最多 6 项" };
    private readonly ComboBox ambiguity = new() { Visibility = Visibility.Collapsed, MinHeight = 40 };
    private readonly TextBlock error = Controls.Text("");
    private DeadlineTask original = null!;
    private readonly TaskService tasks;
    private readonly StateStore store;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly Button deleteButton;
    private readonly StackPanel actions = new();
    private bool creating;
    private string initial = "", initialRules = "";
    private DateTime initialLocal;
    private bool saved, busy, loading;
    public bool WasSaved => saved && !HasChanges;
    public bool WasCreated { get; private set; }
    public bool WasDeleted { get; private set; }
    internal string? TaskId => creating ? null : original.Id;
    internal bool HasChanges => Signature() != initial;
    public EditorWindow(StateStore store, TaskService tasks, AppSettings settings, DeadlineTask? task = null) : base(task is null ? "新建任务" : "编辑任务")
    {
        this.store = store; this.tasks = tasks;
        Add("任务名称", title); Add("截止日期与时间", date);
        var dates = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (label, days) in new[] { ("今天", 0), ("明天", 1), ("一周后", 7) }) { var n = days; var b = Controls.Button(label, () => date.SelectedDate = DateTime.Today.AddDays(n), "SecondaryButton"); b.Margin = new Thickness(0, 0, 8, 4); dates.Children.Add(b); }
        Body.Children.Add(dates);
        var clockRow = new Grid { Margin = new Thickness(0, 8, 0, 0) }; clockRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) }); clockRow.ColumnDefinitions.Add(new ColumnDefinition()); clockRow.Children.Add(time); var clockHint = new TextBlock { Text = "24 小时制 · 当前时区", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(clockHint, 1); clockRow.Children.Add(clockHint); Body.Children.Add(clockRow);
        System.Windows.Automation.AutomationProperties.SetName(time, "截止时间（HH:mm）"); System.Windows.Automation.AutomationProperties.SetName(ambiguity, "重复时刻的 UTC 偏移");
        Body.Children.Add(Controls.Text(TimeZoneInfo.Local.DisplayName, "Small")); Body.Children.Add(ambiguity);
        Add("提醒", hour); Body.Children.Add(at); Add("其他提前分钟数（可选）", custom); Add("备注（可选）", notes);
        error.SetResourceReference(TextBlock.ForegroundProperty, "Error"); error.TextWrapping = TextWrapping.Wrap; error.Margin = new Thickness(0, 12, 0, 0); Body.Children.Add(error);
        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cancel = Controls.Button("取消", Close); footer.Children.Add(cancel); var save = Controls.Button("保存任务", () => Controls.Safely(Save, ShowError), "PrimaryButton", "check"); Grid.SetColumn(save, 1); footer.Children.Add(save); actions.Children.Add(footer);
        deleteButton = Controls.Button("删除任务", () => Controls.Safely(Delete, ShowError), null, "trash"); actions.Children.Add(deleteButton);
        Layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetRow(actions, 2); Layout.Children.Add(actions);
        Closed += (_, _) => { foreach (var field in TransitionFields()) Motion.Stop(field); };
        Loaded += (_, _) => title.Focus();
        date.SelectedDateChanged += (_, _) => { if (!loading) ambiguity.SelectedIndex = -1; }; time.TextChanged += (_, _) => { if (!loading) ambiguity.SelectedIndex = -1; };
        PreviewKeyDown += (_, e) => { if (!date.IsDropDownOpen && e.Key == Key.Enter && (!ReferenceEquals(e.OriginalSource, notes) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) { e.Handled = true; Controls.Safely(Save, ShowError); } };
        Closing += (_, e) => { if (busy) { e.Cancel = true; return; } if (!WasDeleted && HasChanges && MessageBox.Show(this, "放弃尚未保存的修改？", "Re:minder", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true; };
        LoadTask(settings, task);
    }
    internal void LoadTask(AppSettings settings, DeadlineTask? task)
    {
        loading = true;
        try
        {
            date.IsDropDownOpen = false;
            creating = task is null; saved = false; WasCreated = false; WasDeleted = false;
            Heading.Text = creating ? "新建任务" : "编辑任务"; Title = $"{Heading.Text} · Re:minder";
            var local = task is null ? DateTime.Today.AddHours(23).AddMinutes(59) : task.DeadlineUtc.LocalDateTime;
            initialLocal = local;
            original = task ?? new DeadlineTask { CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, DeadlineUtc = DateTimeOffset.UtcNow, ReminderRules = settings.DefaultReminderRules };
            title.Text = task?.Title ?? ""; notes.Text = task?.Notes ?? ""; date.SelectedDate = local.Date; date.Text = local.ToString("d", CultureInfo.CurrentCulture); time.Text = local.ToString("HH:mm");
            hour.IsChecked = original.ReminderRules.Contains(3600); at.IsChecked = original.ReminderRules.Contains(0);
            custom.Text = string.Join(", ", original.ReminderRules.Where(x => x is not 0 and not 3600).Select(x => (x / 60d).ToString("0.##", CultureInfo.InvariantCulture)));
            ambiguity.ItemsSource = null; ambiguity.Visibility = Visibility.Collapsed;
            if (!creating && TimeZoneInfo.Local.IsAmbiguousTime(DateTime.SpecifyKind(local, DateTimeKind.Unspecified)))
            {
                ambiguity.ItemsSource = TimeZoneInfo.Local.GetAmbiguousTimeOffsets(local).Select(x => new OffsetChoice(x)).ToArray(); ambiguity.DisplayMemberPath = "Label";
                ambiguity.SelectedItem = ((OffsetChoice[])ambiguity.ItemsSource).First(x => x.Offset == TimeZoneInfo.Local.GetUtcOffset(original.DeadlineUtc)); ambiguity.Visibility = Visibility.Visible;
            }
            deleteButton.Visibility = creating ? Visibility.Collapsed : Visibility.Visible; error.Text = "";
            initial = Signature(); initialRules = RulesSignature();
            SmoothScroll.Cancel(BodyScroll); BodyScroll.ScrollToTop();
            if (IsLoaded) { foreach (var field in TransitionFields()) Motion.RefreshContent(field); title.Focus(); }
        }
        finally { loading = false; }
    }
    private FrameworkElement[] TransitionFields() => [title, date, time, hour, at, custom, notes, ambiguity];
    internal async Task<bool> TrySaveForSwitchAsync()
    {
        await operations.WaitAsync();
        try
        {
            if (IsLeaving) return false;
            if (!HasChanges) return true;
            await PersistCurrent(); return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { ShowError("未能自动保存，当前修改已保留。" + e.Message); return false; }
        finally { operations.Release(); }
    }
    private void Add(string label, UIElement control) { Body.Children.Add(Controls.Label(label)); Body.Children.Add(control); if (control is TextBox or DatePicker or ComboBox) System.Windows.Automation.AutomationProperties.SetName(control, label); }
    private string DateText => (date.Template?.FindName("PART_TextBox", date) as System.Windows.Controls.Primitives.DatePickerTextBox)?.Text ?? date.Text;
    private string Signature() => $"{title.Text}|{(DateTime.TryParse(DateText, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var value) ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DateText)}|{time.Text}|{hour.IsChecked}|{at.IsChecked}|{custom.Text}|{notes.Text}";
    private string RulesSignature() => $"{hour.IsChecked}|{at.IsChecked}|{custom.Text}";
    private void ShowError(string message)
    {
        error.Text = message; System.Windows.Automation.AutomationProperties.SetLiveSetting(error, System.Windows.Automation.AutomationLiveSetting.Assertive);
        if (IsLoaded) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => { if (IsVisible) error.BringIntoView(); });
    }
    private async Task Save()
    {
        if (!operations.Wait(0)) return;
        try { if (IsLeaving) return; await PersistCurrent(); }
        finally { operations.Release(); }
        Close();
    }
    private DeadlineTask BuildInput()
    {
        if (!DateTime.TryParse(DateText, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var selectedDate) || !TimeOnly.TryParseExact(time.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tod)) throw new ArgumentException("请选择有效日期，并以 HH:mm 输入时间，例如 09:30。");
        var local = DateTime.SpecifyKind(selectedDate.Date.Add(tod.ToTimeSpan()), DateTimeKind.Unspecified); TimeSpan? selectedOffset = ambiguity.SelectedItem is OffsetChoice c ? c.Offset : null;
        var sameDeadline = !creating && local.Date == initialLocal.Date && time.Text.Trim() == initialLocal.ToString("HH:mm");
        if (TimeZoneInfo.Local.IsAmbiguousTime(local) && selectedOffset is null && !sameDeadline)
        {
            ambiguity.ItemsSource = TimeZoneInfo.Local.GetAmbiguousTimeOffsets(local).Select(x => new OffsetChoice(x)).ToArray(); ambiguity.DisplayMemberPath = "Label"; ambiguity.Visibility = Visibility.Visible;
            throw new ArgumentException("夏令时结束使这个时间出现两次，请明确选择时区偏移。");
        }
        var utc = sameDeadline ? original.DeadlineUtc.ToUniversalTime() : DeadlineClock.ToUtc(local.Date, local.TimeOfDay, TimeZoneInfo.Local, selectedOffset);
        var rules = new List<int>(); if (hour.IsChecked == true) rules.Add(3600); if (at.IsChecked == true) rules.Add(0);
        foreach (var token in custom.Text.Split([',', '，'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        { if (!double.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var minutes) || minutes < 0 || minutes > 525600) throw new ArgumentException("提前分钟数须在 0–525600 之间，用逗号分隔。"); rules.Add((int)Math.Round(minutes * 60)); }
        var input = original with { Title = title.Text.Trim(), Notes = notes.Text, DeadlineUtc = utc, InputTimeZoneId = TimeZoneInfo.Local.Id, InputLocalDateTime = utc.LocalDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), ReminderRules = RulesSignature() == initialRules ? original.ReminderRules : rules.Distinct().OrderDescending().ToArray() };
        Validation.Task(input); return input;
    }
    private async Task PersistCurrent()
    {
        var input = BuildInput(); busy = true; Body.IsEnabled = actions.IsEnabled = false;
        try
        {
            await tasks.SaveAsync(input, creating ? null : original.UpdatedAtUtc);
            WasCreated = creating; saved = true; creating = false;
            original = store.Snapshot.Tasks.First(t => t.Id == input.Id);
            initialLocal = original.DeadlineUtc.LocalDateTime; initial = Signature(); initialRules = RulesSignature();
            error.Text = "";
            Heading.Text = "编辑任务"; Title = "编辑任务 · Re:minder"; deleteButton.Visibility = Visibility.Visible;
        }
        finally { busy = false; Body.IsEnabled = actions.IsEnabled = true; }
    }
    private async Task Delete()
    {
        if (creating || !operations.Wait(0)) return;
        try
        {
            if (IsLeaving) return;
            if (Signature() != initial && MessageBox.Show(this, "删除任务会放弃尚未保存的修改，继续？", "Re:minder", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            busy = true; Body.IsEnabled = actions.IsEnabled = false;
            try { await tasks.SetStatusAsync(original.Id, TaskStatus.Deleted, original.UpdatedAtUtc); saved = true; WasDeleted = true; }
            finally { busy = false; Body.IsEnabled = actions.IsEnabled = true; }
        }
        finally { operations.Release(); }
        Close();
    }
    private sealed record OffsetChoice(TimeSpan Offset) { public string Label => $"UTC{(Offset < TimeSpan.Zero ? "−" : "+")}{Offset.Duration():hh\\:mm}"; }
}
