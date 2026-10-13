using System.Windows.Input;
using System.Windows.Threading;

namespace ReMinder.App.UI;

public partial class PanelView : UserControl
{
    private readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly System.Diagnostics.Stopwatch elapsed = new();
    private TimeSpan remaining;
    private int snapshotGeneration, toastGeneration;
    private readonly List<CompletionFeedback> completions = [];
    private ScrollViewer? taskScroll;
    public PanelView()
    {
        InitializeComponent();
        toastTimer.Tick += (_, _) => { if (!Toast.IsMouseOver && !Toast.IsKeyboardFocusWithin) HideToast(); else Pause(); };
        Toast.MouseEnter += (_, _) => Pause(); Toast.GotKeyboardFocus += (_, _) => Pause();
        Toast.MouseLeave += (_, _) => Resume(); Toast.LostKeyboardFocus += (_, _) => Resume();
        DismissToast.Click += (_, _) => HideToast();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.N && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && DataContext is PanelViewModel model) { model.New.Execute(null); e.Handled = true; } };
        Loaded += (_, _) =>
        {
            Motion.PreferenceChanged += MotionChanged;
            taskScroll = FindScrollViewer(TaskList); if (taskScroll is not null) taskScroll.ScrollChanged += TaskScrolled;
            Motion.Appear(this, Motion.Enter, 6); AnimateRows(new Dictionary<TaskItemViewModel, double>());
        };
        Unloaded += (_, _) => { snapshotGeneration++; toastGeneration++; toastTimer.Stop(); ClearCompletions(); Motion.PreferenceChanged -= MotionChanged; if (taskScroll is not null) taskScroll.ScrollChanged -= TaskScrolled; taskScroll = null; Motion.Stop(this); Motion.Stop(Toast); if (Toast.RenderTransform is TranslateTransform offset) Motion.Stop(offset); if (RenderTransform is TranslateTransform panelOffset) Motion.Stop(panelOffset); };
    }
    public void ShowToast(string text, bool canUndo)
    {
        toastGeneration++;
        var alreadyVisible = Toast.Visibility == Visibility.Visible;
        ToastText.Text = text; UndoButton.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed; Toast.Visibility = Visibility.Visible;
        Toast.IsHitTestVisible = true;
        if (alreadyVisible)
        { Motion.Animate(Toast, OpacityProperty, 1, Motion.Release); Motion.Animate(Motion.Offset(Toast), TranslateTransform.YProperty, 0, Motion.Release); }
        else Motion.Appear(Toast, Motion.Release, 8);
        toastTimer.Stop(); elapsed.Reset(); remaining = TimeSpan.FromSeconds(8); Resume();
        System.Windows.Automation.AutomationProperties.SetName(Toast, text);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(Toast, System.Windows.Automation.AutomationLiveSetting.Polite);
    }
    private void HideToast()
    {
        toastTimer.Stop(); elapsed.Reset(); Toast.IsHitTestVisible = false;
        var generation = ++toastGeneration;
        Motion.Disappear(Toast, () => { if (generation == toastGeneration) { Toast.Visibility = Visibility.Collapsed; Toast.Opacity = 1; } });
    }
    public void ApplySnapshot(AppState state)
    {
        if (DataContext is not PanelViewModel model) return;
        var positions = new Dictionary<TaskItemViewModel, double>();
        var previouslyCompleted = model.State.Tasks.Where(t => t.Status == TaskStatus.Completed).Select(t => t.Id).ToHashSet();
        var restored = state.Tasks.Where(t => t.Status == TaskStatus.Pending && previouslyCompleted.Contains(t.Id)).Select(t => t.Id).ToHashSet();
        foreach (var completion in completions.Where(c => state.Settings.Collapsed || state.Tasks.Any(t => t.Id == c.TaskId && t.Status == TaskStatus.Pending)).ToArray())
        { completion.Dispose(); completions.Remove(completion); }
        if (completions.Count == 0) TaskRegion.MinHeight = 0;
        var completedIds = state.Tasks.Where(t => t.Status == TaskStatus.Completed).Select(t => t.Id).ToHashSet();
        var exiting = false;
        if (IsLoaded && Motion.Enabled)
            foreach (var item in model.Tasks)
                if (TaskList.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement { IsVisible: true } row)
                {
                    positions[item] = row.TransformToAncestor(TaskList).Transform(new Point()).Y;
                    var point = row.TranslatePoint(new Point(), CompletionLayer);
                    if (!state.Settings.Collapsed && completedIds.Contains(item.Task.Id) && row.ActualWidth > 0 && row.ActualHeight > 0 && point.Y < TaskRegion.ActualHeight && point.Y + row.ActualHeight > 0)
                    {
                        TaskRegion.MinHeight = Math.Min(TaskRegion.ActualHeight, row.ActualHeight);
                        completions.Add(new CompletionFeedback(item.Task.Id, row, CompletionLayer, CompletionFinished)); exiting = true;
                    }
                }
        model.Apply(state);
        var generation = ++snapshotGeneration;
        if (IsLoaded && Motion.Enabled)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { if (generation == snapshotGeneration && IsLoaded) AnimateRows(positions, exiting ? Motion.Check : 0, restored); });
    }
    private void AnimateRows(Dictionary<TaskItemViewModel, double> positions, int hold = 0, HashSet<string>? restored = null)
    {
        if (DataContext is not PanelViewModel model || !Motion.Enabled) return;
        foreach (var item in model.Tasks)
        {
            if (TaskList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement { IsVisible: true } row) continue;
            if (!positions.TryGetValue(item, out var oldY)) { if (restored?.Contains(item.Task.Id) == true) Motion.RefreshContent(row); else Motion.Appear(row, Motion.Release, 6); continue; }
            var offset = Motion.Offset(row);
            // Include the current visual offset so a second update can replace an in-flight move.
            var newY = row.TransformToAncestor(TaskList).Transform(new Point()).Y - offset.Y;
            Motion.Stop(offset); offset.Y = oldY - newY;
            Motion.Animate(offset, TranslateTransform.YProperty, 0, Motion.Release, hold: hold);
        }
    }
    private void CompletionFinished()
    {
        completions.RemoveAll(c => c.IsDisposed);
        // Keep enough space for the last task's confirmation, then release natural layout.
        if (CompletionLayer.Children.Count == 0) { completions.Clear(); TaskRegion.MinHeight = 0; }
    }
    private void ClearCompletions()
    {
        foreach (var completion in completions.ToArray()) completion.Dispose();
        completions.Clear(); TaskRegion.MinHeight = 0;
    }
    private void MotionChanged() { if (!Motion.Enabled) ClearCompletions(); }
    private void TaskScrolled(object sender, ScrollChangedEventArgs args) { if (args.VerticalChange != 0) ClearCompletions(); }
    private void Pause() { if (!toastTimer.IsEnabled) return; remaining -= elapsed.Elapsed; toastTimer.Stop(); elapsed.Reset(); }
    private void Resume() { if (Toast.Visibility != Visibility.Visible || Toast.IsMouseOver || Toast.IsKeyboardFocusWithin) return; toastTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, remaining.TotalMilliseconds)); elapsed.Restart(); toastTimer.Start(); }
    public void FocusTask(string id)
    {
        if (DataContext is not PanelViewModel model) return;
        var item = model.Tasks.FirstOrDefault(x => x.Task.Id == id); if (item is null) return;
        if (FindScrollViewer(TaskList) is { } viewer) SmoothScroll.Cancel(viewer);
        TaskList.ScrollIntoView(item);
    }
    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is ScrollViewer viewer) return viewer; if (FindScrollViewer(child) is { } nested) return nested; }
        return null;
    }
}
