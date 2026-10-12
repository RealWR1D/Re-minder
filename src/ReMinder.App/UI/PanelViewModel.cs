using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ReMinder.App.UI;

public sealed class RelayCommand(Action<object?> action) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action(parameter);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TaskItemViewModel : Observable
{
    public DeadlineTask Task { get; private set; }
    public string Title => Task.Title;
    public string Absolute { get; private set; } = "";
    public string Compact { get; private set; } = "";
    public string Urgency { get; private set; } = "normal";
    public Brush BadgeBackground => Urgency == "normal" ? Brushes.Transparent : (Brush)Application.Current.Resources[Urgency == "error" ? "ErrorContainer" : "WarningContainer"];
    public Brush BadgeForeground => (Brush)Application.Current.Resources[Urgency == "error" ? "OnErrorContainer" : Urgency == "warning" ? "OnWarningContainer" : "Muted"];
    public TaskItemViewModel(DeadlineTask task) { Task = task; Refresh(task); }
    public void Refresh(DeadlineTask task)
    {
        Task = task;
        var view = DeadlineClock.Format(task.DeadlineUtc, DateTimeOffset.UtcNow); Absolute = view.Absolute; Compact = view.Compact; Urgency = view.Urgency;
        Notify(nameof(Title)); Notify(nameof(Absolute)); Notify(nameof(Compact)); Notify(nameof(Urgency)); Notify(nameof(Task)); Notify(nameof(BadgeBackground)); Notify(nameof(BadgeForeground));
    }
}

public sealed class PanelViewModel : Observable
{
    public ObservableCollection<TaskItemViewModel> Tasks { get; } = [];
    public AppState State { get; private set; } = new();
    public string Date => DateTime.Now.ToString("M月d日 · dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
    public string SummaryTitle { get; private set; } = "";
    public string Countdown { get; private set; } = "";
    public string SummaryDate { get; private set; } = "";
    public string SummaryLabel { get; private set; } = "下一项截止";
    public string PendingLabel => $"{Tasks.Count} 项待办";
    public string OverdueLabel => $"{Tasks.Count(t => t.Task.DeadlineUtc < DateTimeOffset.UtcNow)} 项逾期";
    public bool HasOverdue => Tasks.Any(t => t.Task.DeadlineUtc < DateTimeOffset.UtcNow);
    public bool HasTasks => Tasks.Count > 0;
    public bool Empty => !HasTasks;
    public bool Expanded => !State.Settings.Collapsed;
    public bool Collapsed => State.Settings.Collapsed;
    public string EmptyTitle => State.Tasks.Any(t => t.Status == TaskStatus.Completed) ? "当前没有待办" : "把下一项截止时间放在这里";
    public string EmptySubtitle => State.Tasks.Any(t => t.Status == TaskStatus.Completed) ? "做完的事，已经好好收下。\n准备好了，再开始下一项。" : "留出一点空间，给重要的事。\n添加任务，接下来的时间一眼可见。";
    public string CompletedLabel => $"已完成 {State.Tasks.Count(t => t.Status == TaskStatus.Completed)}";
    public string HostNotice { get; set; } = "";
    public bool HasHostNotice => !string.IsNullOrEmpty(HostNotice);
    public DeadlineTask? SummaryTask { get; private set; }
    public ICommand New { get; }
    public ICommand Edit { get; }
    public ICommand Complete { get; }
    public ICommand Summary { get; }
    public ICommand Menu { get; }
    public ICommand History { get; }
    public ICommand Undo { get; }
    public ICommand RetryDesktop { get; }
    public PanelViewModel(Action<WireMessage> send)
    {
        void Action(string name, object? item = null) { var task = (item as TaskItemViewModel)?.Task ?? item as DeadlineTask; send(new WireMessage { Kind = "command", Action = name, TaskId = task?.Id ?? "", ExpectedVersion = task?.UpdatedAtUtc }); }
        New = new RelayCommand(_ => Action("new")); Edit = new RelayCommand(t => Action("edit", t)); Complete = new RelayCommand(t => Action("complete", t)); Summary = new RelayCommand(_ => { if (SummaryTask is not null) Action("edit", SummaryTask); });
        Menu = new RelayCommand(_ => Action(State.Settings.Collapsed ? "expand" : "menu")); History = new RelayCommand(_ => Action("history")); Undo = new RelayCommand(_ => Action("undo")); RetryDesktop = new RelayCommand(_ => Action("retry-desktop"));
    }
    public void Apply(AppState state)
    {
        State = state;
        var ordered = DeadlineClock.Pending(state.Tasks);
        foreach (var existing in Tasks.Where(t => !ordered.Any(x => x.Id == t.Task.Id)).ToArray()) Tasks.Remove(existing);
        for (var i = 0; i < ordered.Length; i++)
        {
            var task = ordered[i]; var existing = Tasks.FirstOrDefault(x => x.Task.Id == task.Id);
            if (existing is null) Tasks.Insert(i, new TaskItemViewModel(task));
            else { existing.Refresh(task); if (Tasks.IndexOf(existing) != i) Tasks.Move(Tasks.IndexOf(existing), i); }
        }
        RefreshClock();
        foreach (var name in new[] { nameof(PendingLabel), nameof(HasTasks), nameof(Empty), nameof(Expanded), nameof(Collapsed), nameof(EmptyTitle), nameof(EmptySubtitle), nameof(CompletedLabel), nameof(HostNotice), nameof(HasHostNotice), nameof(State) }) Notify(name);
    }
    public void RefreshClock()
    {
        foreach (var t in Tasks) t.Refresh(t.Task);
        SummaryTask = DeadlineClock.Next(State.Tasks, DateTimeOffset.UtcNow);
        var view = SummaryTask is not null ? DeadlineClock.Format(SummaryTask.DeadlineUtc, DateTimeOffset.UtcNow) : null;
        SummaryTitle = SummaryTask?.Title ?? EmptyTitle; Countdown = view?.Detailed ?? ""; SummaryDate = view?.Absolute ?? "";
        SummaryLabel = Tasks.Count > 0 && Tasks.All(t => t.Task.DeadlineUtc < DateTimeOffset.UtcNow) ? $"有 {Tasks.Count} 项逾期" : "下一项截止";
        foreach (var name in new[] { nameof(Date), nameof(SummaryTitle), nameof(Countdown), nameof(SummaryDate), nameof(SummaryLabel), nameof(OverdueLabel), nameof(HasOverdue) }) Notify(name);
    }
}
