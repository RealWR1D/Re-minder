using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ReMinder.App.UI;
using ReMinder.App.Platform;
using ReMinder.Domain;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

internal static class Program
{
    private static readonly List<string> checks = [];
    private static string output = "";
    [STAThread]
    public static int Main(string[] args)
    {
        output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? "artifacts/motion-checks"); Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Console.Error.WriteLine(e.Exception); File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = false, checks, error = e.Exception.ToString() })); app.Shutdown(1); };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/ReMinder;component/UI/Styles.xaml", UriKind.Relative) });
        app.Startup += async (_, _) =>
        {
            try { if (args.Contains("--scroll-only")) await CheckScrolling(); else if (args.Contains("--calendar-only")) await CheckCalendar(); else if (args.Contains("--placement-only")) await CheckPlacement(); else await Run(); File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true })); app.Shutdown(); }
            catch (Exception e) { Console.Error.WriteLine(e); File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = false, checks, error = e.ToString() }, new JsonSerializerOptions { WriteIndented = true })); app.Shutdown(1); }
        };
        return app.Run();
    }
    private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); Console.WriteLine("PASS " + name); }
    private static Window Host(object content) => new() { Content = content, Width = 420, Height = 650, Left = SystemParameters.WorkArea.Left + 20, Top = SystemParameters.WorkArea.Top + 20, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
    private static AppSettings Settings(bool reduced = false, string theme = "light") => new() { ReduceMotion = reduced, ThemeMode = theme };
    private static async Task Run()
    {
        Theme.Apply(Settings());
        var animated = Motion.Enabled;
        var button = new ProbeButton { Content = "测试按钮", Width = 180, Height = 48, Style = (Style)Application.Current.Resources[typeof(Button)] };
        var check = new CheckBox { Content = "测试复选框" };
        var disclosure = new Border { Child = new Border { Height = 100 }, Width = 220, Background = Brushes.Purple };
        var stack = new StackPanel(); stack.Children.Add(button); stack.Children.Add(check); stack.Children.Add(disclosure);
        var host = Host(stack); host.Show(); await Task.Delay(Motion.Enter + 120);
        var surface = (FrameworkElement)button.Template.FindName("Surface", button);
        var scale = (ScaleTransform)surface.RenderTransform;
        var ripple = (Canvas)button.Template.FindName("RippleHost", button);
        var width = button.ActualWidth;
        button.Press(true); await Task.Delay(180);
        Check(Math.Abs(scale.ScaleX - (animated ? Motion.PressScale : 1)) < .001 && button.ActualWidth == width, $"press feedback preserves layout and hit target (scale={scale.ScaleX}, width={button.ActualWidth}, pressed={button.IsPressed}, motion={animated})");
        if (animated) Check(ripple.Children.Count == 1, "press creates clipped ripple");
        await Task.Delay(Motion.RippleExpansion + 100);
        if (animated) Check(ripple.Children.Count == 1 && ((FrameworkElement)ripple.Children[0]).Opacity > .1, "held button keeps the expanded wave visible until release");
        var clicked = false; button.Click += (_, _) => clicked = true; button.ClickNow();
        Check(clicked, "click action executes without waiting for motion");
        button.Press(false); await Task.Delay(Motion.Release / 2);
        if (animated) Check(ripple.Children.Count == 1 && ((FrameworkElement)ripple.Children[0]).Opacity is > 0 and < .13 && scale.ScaleX > Motion.PressScale && scale.ScaleX < 1, "release softly fades the wave while restoring the small scale change");
        await Task.Delay(650);
        Check(Math.Abs(scale.ScaleX - 1) < .001 && !scale.HasAnimatedProperties && ripple.Children.Count == 0, "release resets scale and clears animation clocks/ripple");
        if (animated)
        {
            button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button), 0, Key.Space) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            button.Press(true);
            Check(ripple.Children.Count == 1 && Math.Abs(Canvas.GetLeft(ripple.Children[0]) + ((FrameworkElement)ripple.Children[0]).Width / 2 - surface.ActualWidth / 2) < .01, "keyboard feedback originates at center");
            button.Press(false);
            await Task.Delay(850);
            button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Check(ripple.Children.Count == 1, "Enter key starts click feedback without IsPressed");
            await Task.Delay(850);
        }
        for (var i = 0; i < 5; i++) { button.Press(true); await Task.Delay(15); button.Press(false); }
        if (animated) Check(ripple.Children.Count is >= 2 and <= 6, "rapid clicks overlap bounded waves instead of cutting off the previous wave");
        await Task.Delay(850); Check(Math.Abs(scale.ScaleX - 1) < .001 && ripple.Children.Count == 0, "rapid press/release settles cleanly");
        check.IsChecked = true; await Task.Delay(260);
        var tick = (FrameworkElement)check.Template.FindName("Tick", check);
        Check(Math.Abs(tick.Opacity - 1) < .001, "checkbox reaches checked state");
        Motion.SetReveal(disclosure, false); await Task.Delay(70); Motion.SetReveal(disclosure, true); await Task.Delay(Motion.Enter + 120);
        Check(disclosure.Visibility == Visibility.Visible && double.IsNaN(disclosure.Height) && disclosure.IsHitTestVisible, "interrupted disclosure returns to natural layout");
        Motion.SetReveal(disclosure, false); await Task.Delay(Motion.Enter + 120);
        Check(disclosure.Visibility == Visibility.Collapsed, "disclosure removes final layout space");
        var wrapped = new Border { Child = new TextBlock { Text = new string('测', 100), FontSize = 16, TextWrapping = TextWrapping.Wrap } };
        Motion.SetReveal(wrapped, false); stack.Children.Add(wrapped); await Task.Delay(70);
        Motion.SetReveal(wrapped, true);
        var measuredHeight = (double)wrapped.GetAnimationBaseValue(FrameworkElement.HeightProperty);
        await Task.Delay(Motion.Enter + 120);
        Check((!animated || measuredHeight > 32) && wrapped.ActualHeight > 32, "initially collapsed content measures wrapping at parent width");
        Motion.SetReveal(disclosure, true); button.Press(true); Theme.Apply(Settings(true));
        Check(disclosure.Visibility == Visibility.Visible && double.IsNaN(disclosure.Height) && scale.ScaleX == 1 && ripple.Children.Count == 0, "reduced motion finishes active transitions immediately");
        button.Press(false); button.Press(true); Check(scale.ScaleX == 1 && ripple.Children.Count == 0, "reduced motion keeps feedback without movement"); button.Press(false);
        Theme.Apply(Settings()); button.Press(true); stack.Children.Remove(button); await Task.Delay(80);
        Check(!scale.HasAnimatedProperties && ripple.Children.Count == 0, "unload clears feedback clocks and ripple");
        stack.Children.Insert(0, button); await Task.Delay(80); button.Press(false);
        var combo = new System.Windows.Controls.ComboBox { ItemsSource = new[] { "第一项", "第二项" }, SelectedIndex = 0 }; stack.Children.Add(combo); await Task.Delay(80);
        combo.IsDropDownOpen = true; await Task.Delay(Motion.Content + 120);
        var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
        Check(popup.IsOpen && popup.Child is FrameworkElement { Opacity: 1 }, "dropdown opens and finishes entrance"); combo.IsDropDownOpen = false;
        host.Close();
        await CheckPanel(); await CheckWindows(); await CheckEditors(); await CheckScrolling(); await CheckTextFields(); await CheckCalendar(); await CheckPlacement();
        var field = typeof(Motion).GetField("active", BindingFlags.Static | BindingFlags.NonPublic)!;
        await Task.Delay(Motion.Enter + 120); Check(((System.Collections.IDictionary)field.GetValue(null)!).Count == 0, "all completed/unloaded animations release clocks");
    }
    private static async Task CheckPanel()
    {
        Theme.Apply(Settings());
        var model = new PanelViewModel(_ => { }); var panel = new PanelView { DataContext = model };
        var first = new DeadlineTask { Title = "准备设计稿", DeadlineUtc = DateTimeOffset.UtcNow.AddHours(6) };
        var second = new DeadlineTask { Title = "提交本周计划", DeadlineUtc = DateTimeOffset.UtcNow.AddDays(1) };
        var state = new AppState { Settings = Settings(), Tasks = [first, second] };
        panel.ApplySnapshot(state); var host = Host(panel); host.Show(); await Task.Delay(Motion.Enter + 120);
        var list = (ListBox)panel.FindName("TaskList"); var row = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0);
        var completionLayer = (Canvas)panel.FindName("CompletionLayer");
        var completedState = new AppState { Settings = Settings(), Tasks = [first with { Status = ReMinder.Domain.TaskStatus.Completed }, second] };
        panel.ApplySnapshot(completedState);
        Check(model.Tasks.Count == 1 && (Motion.Enabled ? completionLayer.Children.Count == 1 : completionLayer.Children.Count == 0), "confirmed completion updates business state immediately while retaining a detached feedback visual");
        if (Motion.Enabled)
        {
            await Task.Delay(Motion.Check / 2);
            var ghost = (FrameworkElement)completionLayer.Children[0];
            var completionTick = Find<System.Windows.Shapes.Path>(ghost)!;
            Check(ghost.Opacity == 1 && completionTick.StrokeDashOffset is > 0 and < 10 && ghost.InputHitTest(new Point(28, 44)) is not null, "completed task draws its check before fading and blocks repeat clicks at its old location");
            Capture(panel, "panel-completion-light.png");
            panel.ApplySnapshot(state); await Task.Delay(Motion.EditorReplace + 80);
            Check(completionLayer.Children.Count == 0 && model.Tasks.Count == 2, "undo during completion removes the outgoing visual and restores the task");
            panel.ApplySnapshot(completedState); await Task.Delay(Motion.Check + Motion.CompleteExit + Motion.Release + 100);
            Check(completionLayer.Children.Count == 0 && model.Tasks.Count == 1, "completion feedback and delayed list reflow cleanly finish");
            panel.ApplySnapshot(state); await Task.Delay(Motion.Release + 80);
            panel.ApplySnapshot(completedState); Theme.Apply(Settings(true));
            Check(completionLayer.Children.Count == 0, "turning on reduced motion clears completion overlays immediately");
            Theme.Apply(Settings());
        }
        panel.ApplySnapshot(state); await Task.Delay(Motion.Release + 100);
        row = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0);
        var original = row;
        panel.ApplySnapshot(state); await Task.Delay(60);
        Check(ReferenceEquals(original, list.ItemContainerGenerator.ContainerFromIndex(0)) && row.Opacity == 1, "unchanged snapshot retains rows without replaying entry");
        model.RefreshClock(); Check(row.Opacity == 1, "countdown refresh does not animate cards");
        panel.ApplySnapshot(new AppState { Settings = Settings(), Tasks = [second] }); await Task.Delay(Motion.Enter + 120);
        row = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0);
        Check(model.Tasks.Count == 1 && Motion.Offset(row).Y == 0, "task removal and list reflow settle");
        panel.ApplySnapshot(new AppState { Settings = Settings(), Tasks = [second with { Status = ReMinder.Domain.TaskStatus.Completed }] });
        Check(model.Empty && (Motion.Enabled ? completionLayer.Children.Count == 1 : completionLayer.Children.Count == 0), "last-task completion confirms immediately while its visual remains available for feedback");
        await Task.Delay(Motion.Check + Motion.CompleteExit + Motion.Enter + 100);
        Check(completionLayer.Children.Count == 0 && ((Grid)panel.FindName("TaskRegion")).MinHeight == 0, "last-task confirmation releases its temporary space and overlay");
        panel.ApplySnapshot(new AppState { Settings = Settings(), Tasks = [second] }); await Task.Delay(Motion.Enter + 100);
        panel.ApplySnapshot(new AppState { Settings = Settings() with { Collapsed = true }, Tasks = [second] }); await Task.Delay(50);
        panel.ApplySnapshot(new AppState { Settings = Settings(), Tasks = [second] }); await Task.Delay(Motion.Enter + 120);
        Check(list.IsVisible && list.ActualHeight > 0, "panel collapse/expand can be reversed");
        panel.ShowToast("任务已完成", true); await Task.Delay(Motion.Release + 120);
        var toast = (Border)panel.FindName("Toast"); Check(toast.Visibility == Visibility.Visible && toast.Opacity == 1, "toast entrance completes");
        var actions = (Grid)panel.FindName("PrimaryActions");
        var toastBottom = toast.TranslatePoint(new Point(0, toast.ActualHeight), panel).Y;
        var actionsTop = actions.TranslatePoint(new Point(), panel).Y;
        Check(toastBottom <= actionsTop && actions.IsHitTestVisible && panel.ActualHeight <= 560, "undo notice occupies its own row above always-accessible primary actions");
        Capture(panel, "panel-toast-light.png");
        var timer = (System.Windows.Threading.DispatcherTimer)typeof(PanelView).GetField("toastTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        toast.RaiseEvent(new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
        Check(!timer.IsEnabled, "toast pauses on hover");
        toast.RaiseEvent(new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
        Check(timer.IsEnabled, "toast resumes with remaining time");
        ((Button)panel.FindName("DismissToast")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(35); panel.ShowToast("已撤销", false); await Task.Delay(Motion.Enter + 120);
        Check(toast.Visibility == Visibility.Visible && toast.Opacity == 1 && !timer.Interval.Equals(TimeSpan.Zero), "new toast replaces pending exit safely");
        ((Button)panel.FindName("DismissToast")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(Motion.Exit + 120);
        Check(toast.Visibility == Visibility.Collapsed && !timer.IsEnabled, "dismissed toast stops timer and hides");
        panel.ApplySnapshot(new AppState { Settings = Settings() }); await Task.Delay(850);
        Check(model.Empty && list.Visibility == Visibility.Collapsed, "last task transitions to empty state");
        Theme.Apply(Settings(true)); panel.ApplySnapshot(state); await Task.Delay(60);
        Check(list.Visibility == Visibility.Visible && list.ActualHeight > 0, "reduced motion displays task list immediately");
        foreach (var theme in new[] { "light", "dark" })
        {
            Theme.Apply(Settings(true, theme)); panel.ApplySnapshot(state); host.UpdateLayout();
            Capture(panel, "panel-" + theme + ".png");
            Check(panel.ActualWidth == 360 && panel.ActualHeight <= 560, theme + " panel stays within bounds");
        }
        host.Close(); Check(!timer.IsEnabled, "panel unload stops toast timer");
    }
    private static async Task CheckWindows()
    {
        Theme.Apply(Settings());
        var window = new FrameWindow("动效检查") { Left = SystemParameters.WorkArea.Left + 20, Top = SystemParameters.WorkArea.Top + 20, ShowActivated = false, ShowInTaskbar = false };
        var cancel = true; var closingCalls = 0; var closed = false;
        window.Closing += (_, e) => { closingCalls++; e.Cancel = cancel; }; window.Closed += (_, _) => closed = true;
        window.Show(); await Task.Delay(Motion.Enter + 120); var surface = (FrameworkElement)window.Content;
        window.Close(); Check(window.IsVisible && surface.IsHitTestVisible && surface.Opacity == 1 && !closed, "canceled close preserves editable window");
        cancel = false; window.Close(); await Task.Delay(Motion.Enter + 120);
        Check(closed && closingCalls == 2, "approved close animates once without repeating validation");
        var immediate = new FrameWindow("立即关闭") { Left = SystemParameters.WorkArea.Left + 20, Top = SystemParameters.WorkArea.Top + 20, ShowActivated = false, ShowInTaskbar = false }; immediate.Show(); immediate.Close(); await Task.Delay(Motion.Enter + 120);
        Check(!immediate.IsVisible, "close during entrance is safe");
        Theme.Apply(Settings(true)); var reduced = new FrameWindow("减少动态效果") { Left = SystemParameters.WorkArea.Left + 20, Top = SystemParameters.WorkArea.Top + 20, ShowActivated = false, ShowInTaskbar = false }; reduced.Show(); reduced.Close(); Check(!reduced.IsVisible, "reduced motion closes immediately");
    }
    private static void Capture(FrameworkElement element, string name)
    {
        var width = element.ActualWidth; var height = element.ActualHeight;
        // Remove the test host's centering offset before capturing this control alone.
        element.Arrange(new Rect(0, 0, width, height));
        var image = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32); image.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) return item; if (Find<T>(child) is { } nested) return nested; }
        return null;
    }
    private static async Task CheckEditors()
    {
        Theme.Apply(Settings());
        var store = new StateStore(Path.Combine(output, "test-data", "editors-" + Guid.NewGuid().ToString("N"))); await store.LoadAsync();
        var service = new TaskService(store); var now = DateTimeOffset.UtcNow;
        foreach (var name in new[] { "任务 A", "任务 B", "任务 C" }) await service.SaveAsync(new DeadlineTask { Title = name, CreatedAtUtc = now, UpdatedAtUtc = now, DeadlineUtc = now.AddDays(2) }, null);
        var items = store.Snapshot.Tasks.ToArray(); var shows = 0; var maxWindows = 0;
        var session = new EditorSession(store, service, window =>
        {
            shows++; window.Left = SystemParameters.WorkArea.Left + 20; window.Top = SystemParameters.WorkArea.Top + 20; window.ShowInTaskbar = false; window.ShowActivated = false; window.Show();
            maxWindows = Math.Max(maxWindows, Application.Current.Windows.OfType<EditorWindow>().Count());
        }, (_, _) => { });
        await session.OpenAsync(items[0].Id); await Task.Delay(Motion.Enter + 100);
        var editor = session.Current!; var title = Field<TextBox>(editor, "title"); var notes = Field<TextBox>(editor, "notes");
        title.Text = "任务 A 已修改"; notes.Text = "切换时应保存的备注";
        Check(await session.OpenAsync(items[1].Id) && ReferenceEquals(editor, session.Current) && editor.TaskId == items[1].Id && shows == 1, "switch task reuses the same editor window");
        Check(Field<StackPanel>(editor, "Body").Opacity == 1 && Field<StackPanel>(editor, "actions").Opacity == 1 && title.Opacity >= .72, "editor replacement keeps the shell and fixed actions visible without a whole-form flash");
        var savedA = store.Snapshot.Tasks.First(t => t.Id == items[0].Id);
        Check(savedA.Title == "任务 A 已修改" && savedA.Notes == "切换时应保存的备注" && savedA.ScheduleRevision == items[0].ScheduleRevision, "switch persists title/notes before replacing the form");
        var version = store.Snapshot.StateVersion;
        await session.OpenAsync(items[0].Id); await session.OpenAsync(items[1].Id);
        Check(store.Snapshot.StateVersion == version, "unchanged navigation does not write task data");
        title.Text = "任务 B 草稿";
        await session.OpenAsync(items[1].Id);
        Check(title.Text == "任务 B 草稿" && editor.HasChanges && store.Snapshot.StateVersion == version, "reopening current task preserves its draft");
        var storageGate = Field<System.Threading.SemaphoreSlim>(store, "gate"); await storageGate.WaitAsync();
        var intermediate = session.OpenAsync(items[2].Id); var latest = session.OpenAsync(items[0].Id); storageGate.Release();
        await Task.WhenAll(intermediate, latest);
        Check(!intermediate.Result && latest.Result && editor.TaskId == items[0].Id && shows == 1 && store.Snapshot.Tasks.First(t => t.Id == items[1].Id).Title == "任务 B 草稿", "rapid navigation saves once and shows the latest requested task");
        title.Text = "";
        Check(!await session.OpenAsync(items[1].Id) && editor.TaskId == items[0].Id && title.Text == "" && Field<TextBlock>(editor, "error").Text.Contains("当前修改已保留"), "invalid auto-save blocks switching and preserves the draft");
        editor.LoadTask(Settings(), store.Snapshot.Tasks.First(t => t.Id == items[0].Id));
        title.Text = "本地冲突草稿";
        var storedA = store.Snapshot.Tasks.First(t => t.Id == items[0].Id);
        await service.SaveAsync(storedA with { Title = "外部最新版本" }, storedA.UpdatedAtUtc);
        Check(!await session.OpenAsync(items[1].Id) && title.Text == "本地冲突草稿" && store.Snapshot.Tasks.First(t => t.Id == items[0].Id).Title == "外部最新版本", "concurrent update is not overwritten during auto-save");
        editor.LoadTask(Settings(), store.Snapshot.Tasks.First(t => t.Id == items[0].Id)); title.Text = "写入失败时保留的草稿";
        using (var locked = new FileStream(store.DataPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(!await session.OpenAsync(items[1].Id) && editor.TaskId == items[0].Id && title.Text == "写入失败时保留的草稿", "disk failure keeps current form and input");
        Check(await session.OpenAsync(items[1].Id) && store.Snapshot.Tasks.First(t => t.Id == items[0].Id).Title == "写入失败时保留的草稿", "retry after disk failure saves and then switches");
        var count = store.Snapshot.Tasks.Count; version = store.Snapshot.StateVersion;
        await session.OpenAsync(null); await session.OpenAsync(items[0].Id);
        Check(store.Snapshot.Tasks.Count == count && store.Snapshot.StateVersion == version, "untouched blank new-task form can be replaced without creating a task");
        await session.OpenAsync(null); title.Text = "新建草稿自动保存"; notes.Text = "新建备注";
        await session.OpenAsync(items[1].Id);
        Check(store.Snapshot.Tasks.Count == count + 1 && store.Snapshot.Tasks.Any(t => t.Title == "新建草稿自动保存" && t.Notes == "新建备注"), "new-task draft is saved before opening another task");
        title.Text = "手动保存后立即切换";
        var manualSave = (Task)typeof(EditorWindow).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!;
        var switchWhileSaving = session.OpenAsync(items[2].Id); await Task.WhenAll(manualSave, switchWhileSaving);
        Check(switchWhileSaving.Result && session.Current!.TaskId == items[2].Id && maxWindows == 1 && store.Snapshot.Tasks.First(t => t.Id == items[1].Id).Title == "手动保存后立即切换", "switch during manual save waits for closing and keeps one editor");
        var closing = session.Current!; await Task.Delay(Motion.Enter + 100);
        Capture((FrameworkElement)closing.Content, "editor-reused.png");
        closing.Close(); await closing.WhenClosed;
        Check(session.Current is null && maxWindows == 1, "editor session releases the closed window");
    }
    private static async Task CheckScrolling()
    {
        Theme.Apply(Settings());
        Check(SmoothScroll.Duration(24) < SmoothScroll.Duration(240) && SmoothScroll.Duration(240) < SmoothScroll.Duration(1000), "wheel settling time scales with travel distance within a bounded duration");
        var panel = new PanelView { DataContext = new PanelViewModel(_ => { }) };
        var tasks = Enumerable.Range(0, 200).Select(i => new DeadlineTask { Title = $"滚动任务 {i + 1}", DeadlineUtc = DateTimeOffset.UtcNow.AddDays(1).AddMinutes(i) }).ToList();
        panel.ApplySnapshot(new AppState { Settings = Settings(), Tasks = tasks });
        var host = Host(panel); host.Show(); await Task.Delay(Motion.Enter + 200);
        var list = (ListBox)panel.FindName("TaskList"); var viewer = Find<ScrollViewer>(list)!;
        Check(VirtualizingPanel.GetScrollUnit(list) == ScrollUnit.Pixel && viewer.ScrollableHeight > 500 && viewer.ViewportHeight > 40, "task list uses pixel scrolling with a real viewport");
        var realized = tasks.Select((_, i) => list.ItemContainerGenerator.ContainerFromIndex(i)).Count(x => x is not null);
        Check(realized < tasks.Count / 2, "pixel scrolling retains list virtualization");
        var bar = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer); bar.ApplyTemplate();
        var track = (Track)bar.Template.FindName("PART_Track", bar); var thumb = track.Thumb;
        Check(thumb.ActualWidth >= 12 && thumb.InputHitTest(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2)) is not null, $"scrollbar thumb has a draggable transparent hit area (thumb={thumb.ActualWidth}x{thumb.ActualHeight}, bar={bar.ActualWidth}, track={track.ActualWidth}, visible={thumb.IsVisible}, hit={thumb.InputHitTest(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2)) is not null})");
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        thumb.RaiseEvent(new DragDeltaEventArgs(0, 25) { RoutedEvent = Thumb.DragDeltaEvent }); await Task.Delay(100);
        Check(viewer.VerticalOffset > 0, "drag delta moves the actual task list");
        thumb.RaiseEvent(new DragCompletedEventArgs(0, 25, false) { RoutedEvent = Thumb.DragCompletedEvent });
        SmoothScroll.Cancel(viewer); viewer.ScrollToTop(); await Task.Delay(100);
        Wheel(viewer, -120); await Task.Delay(130);
        var step = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight * .9 : SystemParameters.WheelScrollLines * 24d;
        Check(viewer.VerticalOffset > 0 && viewer.VerticalOffset < step - .1, "wheel traverses intermediate pixel offsets instead of jumping");
        Wheel(viewer, -120); await Task.Delay(Motion.Scroll + 150);
        Check(Math.Abs(viewer.VerticalOffset - step * 2) < 1, "continuous wheel input accumulates the target offset");
        SmoothScroll.Cancel(viewer); viewer.ScrollToTop(); await Task.Delay(100);
        Wheel(viewer, -120); Wheel(viewer, -120); await Task.Delay(180); var beforeReverse = viewer.VerticalOffset;
        Wheel(viewer, 120); await Task.Delay(150);
        Check(viewer.VerticalOffset < beforeReverse, "reverse wheel input turns around from the current position");
        await Task.Delay(Motion.Scroll + 100);
        Wheel(viewer, -120); await Task.Delay(120);
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        thumb.RaiseEvent(new DragDeltaEventArgs(0, 20) { RoutedEvent = Thumb.DragDeltaEvent }); await Task.Delay(120);
        thumb.RaiseEvent(new DragCompletedEventArgs(0, 20, false) { RoutedEvent = Thumb.DragCompletedEvent });
        var dragged = viewer.VerticalOffset; await Task.Delay(Motion.Scroll + 100);
        Check(Math.Abs(viewer.VerticalOffset - dragged) < 1, "thumb dragging cancels wheel animation without fighting the pointer");
        SmoothScroll.Cancel(viewer); viewer.ScrollToTop(); await Task.Delay(100);
        Wheel(viewer, -120); await Task.Delay(120); panel.FocusTask(tasks[180].Id); await Task.Delay(Motion.Scroll + 180);
        var focused = list.ItemContainerGenerator.ContainerFromIndex(180) as FrameworkElement;
        var focusedY = focused?.TransformToAncestor(viewer).Transform(new Point()).Y ?? -1;
        Check(focused is { IsVisible: true } && focusedY >= -1 && focusedY < viewer.ViewportHeight, "programmatic task focus takes priority over wheel motion");
        Theme.Apply(Settings(true)); viewer.ScrollToTop(); await Task.Delay(100); Wheel(viewer, -120); await Task.Delay(100);
        Check(Math.Abs(viewer.VerticalOffset - step) < 1 && !viewer.HasAnimatedProperties, "reduced motion scrolls immediately without a clock");
        viewer.ScrollToBottom(); await Task.Delay(100); Wheel(viewer, -120); await Task.Delay(100);
        Check(Math.Abs(viewer.VerticalOffset - viewer.ScrollableHeight) < 1, "scrolling at the bottom never overshoots");
        Theme.Apply(Settings()); Wheel(viewer, 120); await Task.Delay(90); host.Close(); await Task.Delay(80);
        Check(!viewer.HasAnimatedProperties, "scroll viewer unload cancels the wheel clock");
    }
    private static void Wheel(ScrollViewer viewer, int delta) => viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
    private static async Task CheckTextFields()
    {
        Theme.Apply(Settings()); var animated = Motion.Enabled;
        var first = new TextBox { Text = "选中文字并继续输入", Width = 350, TabIndex = 0, Margin = new Thickness(0, 24, 0, 0) };
        var second = new TextBox { Text = "多行备注\n第二行", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Width = 350, MinHeight = 80, TabIndex = 1, Margin = new Thickness(0, 16, 0, 16) };
        var date = new DatePicker { SelectedDate = DateTime.Today, Width = 350, TabIndex = 2 };
        var park = new Button { Content = "移出焦点", TabIndex = 3, Margin = new Thickness(0, 16, 0, 0) };
        var root = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Surface");
        root.Children.Add(first); root.Children.Add(second); root.Children.Add(date); root.Children.Add(park);
        var host = Host(root); host.Show(); host.Activate(); park.Focus(); await Task.Delay(Motion.Enter + 120);
        var outline = (Border)first.Template.FindName("FocusOutline", first);
        var tint = (Border)first.Template.FindName("FocusTint", first);
        var content = (ScrollViewer)first.Template.FindName("PART_ContentHost", first);
        var contentOrigin = content.TranslatePoint(new Point(), root); var size = first.RenderSize;
        first.Select(2, 3); first.Focus(); await Task.Delay(Motion.Focus / 3);
        if (animated) Check(outline.Opacity is > 0 and < 1 && tint.Opacity is > 0 and < .035, "text field focus passes through intermediate outline and tint states");
        Check(first.IsKeyboardFocusWithin && first.SelectionStart == 2 && first.SelectionLength == 3 && first.RenderSize == size && content.TranslatePoint(new Point(), root) == contentOrigin, "focus animation preserves text selection and content geometry");
        first.AppendText("追加"); Check(first.Text.EndsWith("追加"), "text input updates immediately during focus animation");
        await Task.Delay(Motion.Focus + 80);
        Check(outline.Opacity == 1 && Math.Abs(tint.Opacity - .035) < .0001 && !outline.HasAnimatedProperties, "focused text field settles without retaining animation clocks");
        var moved = first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); await Task.Delay(Motion.Focus + 80);
        var secondOutline = (Border)second.Template.FindName("FocusOutline", second);
        Check(moved && second.IsKeyboardFocusWithin && outline.Opacity == 0 && secondOutline.Opacity == 1, "keyboard traversal transfers focus feedback to multiline text field");
        for (var i = 0; i < 4; i++) { first.Focus(); await Task.Delay(20); second.Focus(); await Task.Delay(20); }
        first.Focus(); await Task.Delay(Motion.Focus + 80);
        Check(outline.Opacity == 1 && secondOutline.Opacity == 0 && first.RenderSize == size, "rapid focus changes settle on the latest field without layout movement");
        date.ApplyTemplate(); var dateText = (System.Windows.Controls.Primitives.DatePickerTextBox)date.Template.FindName("PART_TextBox", date);
        dateText.Focus(); await Task.Delay(Motion.Focus + 80);
        Check(TextFieldMotion.GetEnabled(date) && !TextFieldMotion.GetEnabled(dateText) && ((Border)date.Template.FindName("FocusOutline", date)).Opacity == 1, "date input animates a single shared outline around text and icon");
        first.Focus(); await Task.Delay(20); Theme.Apply(Settings(true));
        Check(outline.Opacity == 1 && !outline.HasAnimatedProperties && !tint.HasAnimatedProperties, "reduced motion finishes input focus feedback immediately");
        first.IsEnabled = false;
        Check(outline.Opacity == 0 && tint.Opacity == 0, "disabled input removes focus emphasis");
        first.IsEnabled = true; Theme.Apply(Settings()); first.Focus(); await Task.Delay(30); root.Children.Remove(first); await Task.Delay(60);
        Check(outline.Opacity == 0 && tint.Opacity == 0 && !outline.HasAnimatedProperties && !tint.HasAnimatedProperties, "input unload clears focus overlays and clocks");
        root.Children.Insert(0, first); await Task.Delay(60); first.Focus(); await Task.Delay(Motion.Focus + 80);
        Check(outline.Opacity == 1, "input reload reconnects focus feedback");
        foreach (var theme in new[] { "light", "dark" }) { Theme.Apply(Settings(true, theme)); host.UpdateLayout(); Capture(root, "input-focus-" + theme + ".png"); }
        host.Close();
    }
    private static async Task CheckCalendar()
    {
        Theme.Apply(Settings()); var animated = Motion.Enabled;
        var date = new DatePicker { SelectedDate = DateTime.Today, Width = 350 };
        var check = new CheckBox { Content = "提前 1 小时" };
        var quick = Controls.Button("今天", () => date.SelectedDate = DateTime.Today, "SecondaryButton");
        quick.HorizontalAlignment = HorizontalAlignment.Left;
        var root = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Surface");
        root.Children.Add(date); root.Children.Add(quick); root.Children.Add(check);
        var host = Host(root); host.Show(); host.Activate(); await Task.Delay(Motion.Enter + 120);
        check.Focus();
        Check(quick.FocusVisualStyle is null && check.FocusVisualStyle is null && date.FocusVisualStyle is null && check.Template.FindName("Focus", check) is null, "Alt/keyboard focus has no default dotted or full-row checkbox frame");
        var halo = (Border)check.Template.FindName("FocusHalo", check);
        Check(halo.CornerRadius.TopLeft == 20 && halo.ActualWidth == 40 && halo.Opacity == .12, "checkbox keyboard focus uses a circular state layer confined to its target");
        var tick = (System.Windows.Shapes.Path)check.Template.FindName("Tick", check);
        var fill = (Border)check.Template.FindName("CheckFill", check);
        check.IsChecked = true; await Task.Delay(Motion.Check / 3);
        if (animated) Check(tick.StrokeDashOffset is > 0 and < 10 && fill.Opacity is > 0 and < 1, "checking progressively draws the tick and fills the checkbox");
        await Task.Delay(Motion.Check + 80);
        Check(tick.StrokeDashOffset == 0 && tick.Opacity == 1 && fill.Opacity == 1, "checked state finishes with a complete tick and theme fill");
        check.IsChecked = false; await Task.Delay(Motion.Check / 3);
        if (animated) Check(tick.StrokeDashOffset is > 0 and < 10 && tick.Opacity is > 0 and < 1 && fill.Opacity is > 0 and < 1, "unchecking retracts the tick and fades the fill");
        for (var i = 0; i < 4; i++) { check.IsChecked = true; await Task.Delay(15); check.IsChecked = false; await Task.Delay(15); }
        await Task.Delay(Motion.Check + 80);
        Check(tick.StrokeDashOffset == 10 && tick.Opacity == 0 && fill.Opacity == 0 && !tick.HasAnimatedProperties, "rapid toggles settle on the latest unchecked value and release clocks");
        Theme.Apply(Settings(true)); check.IsChecked = true;
        Check(tick.StrokeDashOffset == 0 && fill.Opacity == 1 && !tick.HasAnimatedProperties, "reduced motion shows the complete checkbox state immediately");
        quick.Focus(); var ring = (Border)quick.Template.FindName("Ring", quick);
        var origin = ring.TranslatePoint(new Point(), quick);
        Check(origin.X >= 0 && origin.Y >= 0 && origin.X + ring.ActualWidth <= quick.ActualWidth && origin.Y + ring.ActualHeight <= quick.ActualHeight, "quick-date focus accent stays inside the button bounds without clipping");
        var box = (Border)date.Template.FindName("Box", date);
        var dateText = (DatePickerTextBox)date.Template.FindName("PART_TextBox", date);
        var calendarButton = (Button)date.Template.FindName("PART_Button", date);
        Check(box.CornerRadius.TopLeft == 12 && dateText.BorderThickness == new Thickness(0) && calendarButton.Content is Icon { Kind: "calendar", Width: 24, Height: 24 }, $"date field has one rounded container and a 24-DIP vector calendar icon (radius={box.CornerRadius}, border={dateText.BorderThickness}, icon={calendarButton.Content}, width={(calendarButton.Content as Icon)?.Width}, height={(calendarButton.Content as Icon)?.Height})");
        Theme.Apply(Settings()); calendarButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(Motion.Content + 120);
        var popup = (Popup)date.Template.FindName("PART_Popup", date);
        var calendar = (Calendar)popup.Child;
        Check(popup.IsOpen && date.IsDropDownOpen && calendar.Style == Application.Current.Resources["MaterialCalendar"], "calendar icon opens the custom M3 calendar through the native DatePicker");
        var item = (CalendarItem)calendar.Template.FindName("PART_CalendarItem", calendar);
        var month = (Grid)item.Template.FindName("PART_MonthView", item);
        var year = (Grid)item.Template.FindName("PART_YearView", item);
        var days = month.Children.OfType<CalendarDayButton>().ToArray();
        var selected = days.Single(d => d.IsSelected);
        var selectedFill = (Border)selected.Template.FindName("SelectedFill", selected);
        Check(days.Length == 42 && selectedFill.CornerRadius.TopLeft == 20 && selectedFill.Opacity == 1 && selected.FocusVisualStyle is null, "calendar has 42 circular day targets and a filled selected date without square outlines");
        Check(((Border)days.Single(d => d.IsToday).Template.FindName("TodayOutline", days.Single(d => d.IsToday))).Opacity == 1, "today is distinguished by a circular theme outline");
        var next = (Button)item.Template.FindName("PART_NextButton", item);
        var previous = (Button)item.Template.FindName("PART_PreviousButton", item);
        var initialMonth = calendar.DisplayDate;
        next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(Motion.Content / 3);
        // A newly invalidated popup may need its first render before its animation clock starts.
        for (var frame = 0; animated && month.Opacity == 0 && frame < 20; frame++) await Task.Delay(16);
        if (animated) Check(month.Opacity is > 0 and < 1, $"month navigation passes through a short content transition (opacity={month.Opacity}, date={calendar.DisplayDate}, enabled={CalendarMotion.GetEnabled(calendar)}, motion={Motion.Enabled}, visible={month.IsVisible})");
        await Task.Delay(Motion.Content + 80);
        Check(calendar.DisplayDate.Month == initialMonth.AddMonths(1).Month && month.Opacity == 1, "next-month navigation updates dates and settles the transition");
        CheckCalendarCells(calendar, days, "first next month");
        Capture(calendar, "calendar-next-month.png");
        next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(Motion.CalendarPage + Motion.Check + 80);
        CheckCalendarCells(calendar, days, "second next month");
        previous.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(Motion.CalendarPage + Motion.Check + 80);
        previous.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(Motion.Content + 80);
        Check(calendar.DisplayDate.Month == initialMonth.Month, "previous-month navigation returns to the correct month");
        CheckCalendarCells(calendar, days, "returned original month");
        if (animated)
        {
            next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(Motion.CalendarPage / 3);
            var forwardX = ((TranslateTransform)month.RenderTransform).X;
            previous.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            // WPF evaluates newly attached animation clocks on the next render tick.
            await Task.Delay(32);
            Check(forwardX > 0 && month.Opacity >= .6499 && ((TranslateTransform)month.RenderTransform).X > 0, $"rapid reverse month navigation continues from the current horizontal offset without blanking content (before={forwardX}, after={((TranslateTransform)month.RenderTransform).X}, opacity={month.Opacity})");
            await Task.Delay(Motion.CalendarPage + Motion.Check + 80); CheckCalendarCells(calendar, days, "rapid reversed month");
            next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(32); Theme.Apply(Settings(true));
            Check(month.Opacity == 1 && ((TranslateTransform)month.RenderTransform).X == 0 && !month.HasAnimatedProperties, "reduced motion settles an active calendar page transition immediately");
            await Task.Delay(Motion.Check + 80); CheckCalendarCells(calendar, days, "reduced-motion next month");
            previous.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Theme.Apply(Settings());
        }
        var header = (Button)item.Template.FindName("PART_HeaderButton", item);
        header.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(Motion.Content + 80);
        Check(calendar.DisplayMode == CalendarMode.Year && year.IsVisible && !month.IsVisible && year.Children.OfType<CalendarButton>().Count() == 12, "month header opens the styled twelve-month year view");
        header.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(Motion.Content + 80);
        Check(calendar.DisplayMode == CalendarMode.Decade && year.IsVisible, "year header preserves decade navigation");
        calendar.DisplayMode = CalendarMode.Month; calendar.DisplayDate = DateTime.Today;
        await Task.Delay(Motion.Content + 80);
        var blockedDate = DateTime.Today.AddDays(2); date.BlackoutDates.Add(new CalendarDateRange(blockedDate));
        var blocked = days.Single(d => d.DataContext is DateTime value && value.Date == blockedDate);
        blocked.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(80);
        Check(blocked.IsBlackedOut && date.SelectedDate == DateTime.Today, "blackout dates remain unselectable with the custom day template");
        foreach (var theme in new[] { "light", "dark" })
        {
            Theme.Apply(Settings(true, theme)); host.UpdateLayout(); calendar.UpdateLayout();
            Capture(calendar, "calendar-" + theme + ".png"); Capture(root, "m3-controls-" + theme + ".png");
        }
        var chosen = days.Single(d => d.DataContext is DateTime value && value.Date == DateTime.Today.AddDays(1));
        chosen.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(80);
        Check(date.SelectedDate == DateTime.Today.AddDays(1), "day selection updates the DatePicker and its text");
        chosen.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent }); await Task.Delay(80);
        Check(!date.IsDropDownOpen && !popup.IsOpen, "confirming a day closes the popup");
        calendarButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(80);
        var activeDay = days.Single(d => d.IsSelected);
        var originalSelection = date.SelectedDate;
        activeDay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(activeDay), 0, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
        Check(date.SelectedDate > originalSelection && date.SelectedDate != blockedDate, "arrow-key date navigation advances while skipping blackout dates");
        activeDay = days.Single(d => d.IsSelected);
        activeDay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(activeDay), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }); await Task.Delay(80);
        Check(!popup.IsOpen && host.IsVisible && date.SelectedDate == originalSelection, "Escape restores the original date and closes the calendar without closing its window");
        dateText.Text = DateTime.Today.AddDays(3).ToString("d", System.Globalization.CultureInfo.CurrentCulture); quick.Focus(); await Task.Delay(80);
        // Native DatePicker commits text on Enter or focus loss.
        dateText.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dateText), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        Check(date.SelectedDate == DateTime.Today.AddDays(3), "typed dates retain native parsing and commit behavior");
        host.Close(); await Task.Delay(Motion.Content + 80);
        Check(!tick.HasAnimatedProperties && !selectedFill.HasAnimatedProperties && !month.HasAnimatedProperties, "calendar and checkbox unload release all owned animation clocks");
        var store = new StateStore(Path.Combine(output, "test-data", "calendar-" + Guid.NewGuid().ToString("N"))); await store.LoadAsync();
        var editor = new EditorWindow(store, new TaskService(store), Settings()) { ShowInTaskbar = false, ShowActivated = false };
        editor.Show(); await Task.Delay(80); var editorDate = Field<DatePicker>(editor, "date"); editorDate.IsDropDownOpen = true; await Task.Delay(80);
        var version = store.Snapshot.StateVersion;
        editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent }); await Task.Delay(80);
        Check(editor.IsVisible && editorDate.IsDropDownOpen && store.Snapshot.StateVersion == version && Field<TextBlock>(editor, "error").Text == "", "editor Enter does not save the form while navigating the calendar");
        editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }); await Task.Delay(80);
        Check(editor.IsVisible && !editorDate.IsDropDownOpen, "editor Escape dismisses the calendar before closing the editor");
        var editorOriginalDate = editorDate.SelectedDate;
        editorDate.IsDropDownOpen = true; await Task.Delay(80);
        var editorCalendar = (Calendar)((Popup)editorDate.Template.FindName("PART_Popup", editorDate)).Child;
        var editorItem = (CalendarItem)editorCalendar.Template.FindName("PART_CalendarItem", editorCalendar);
        var editorDays = ((Grid)editorItem.Template.FindName("PART_MonthView", editorItem)).Children.OfType<CalendarDayButton>();
        var editorDay = editorDays.Single(d => d.IsSelected);
        editorDay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editorDay), 0, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
        editorDay = editorDays.Single(d => d.IsSelected);
        editorDay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editorDay), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }); await Task.Delay(80);
        Check(editor.IsVisible && !editorDate.IsDropDownOpen && editorDate.SelectedDate == editorOriginalDate, "calendar Escape in the editor preserves native selection rollback");
        editorDate.IsDropDownOpen = true; await Task.Delay(80); editorDay = editorDays.Single(d => d.IsSelected);
        editorDay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editorDay), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent }); await Task.Delay(80);
        Check(editor.IsVisible && !editorDate.IsDropDownOpen && store.Snapshot.StateVersion == version && Field<TextBlock>(editor, "error").Text == "", "calendar Enter confirms the date without saving or closing the editor");
        editor.Close(); await editor.WhenClosed;
    }
    private static void CheckCalendarCells(Calendar calendar, CalendarDayButton[] days, string scene)
    {
        var correct = days.All(day =>
        {
            if (day.DataContext is not DateTime value) return false;
            var selected = calendar.SelectedDates.Contains(value.Date);
            var today = value.Date == DateTime.Today;
            var inactive = value.Month != calendar.DisplayDate.Month || value.Year != calendar.DisplayDate.Year;
            double Opacity(string part) => ((FrameworkElement)day.Template.FindName(part, day)).Opacity;
            return day.IsSelected == selected && day.IsToday == today && day.IsInactive == inactive
                && Math.Abs(Opacity("SelectedFill") - (selected ? 1 : 0)) < .001
                && Math.Abs(Opacity("SelectedText") - (selected ? 1 : 0)) < .001
                && Math.Abs(Opacity("TodayOutline") - (today ? 1 : 0)) < .001
                && Math.Abs(Opacity("DayContent") - (day.IsBlackedOut ? .28 : inactive && !selected ? .45 : 1)) < .001;
        });
        Check(correct, scene + ": every recycled cell's highlight, today outline and inactive tint match its actual date without pointer input");
    }
    private static async Task CheckPlacement()
    {
        Theme.Apply(Settings(true));
        foreach (var (scale, expected) in new[] { (1d, 24), (1.25, 30), (1.5, 36), (2d, 48) })
            Check(PlacementService.SurfaceTop(-200, 24, scale) + (int)(FrameWindow.SurfaceInsetDip * scale) == -200 + expected,
                $"visible top margin stays correct at {scale * 100}% scale with a negative monitor origin");
        var settings = Settings(true) with { PanelWidthDip = 320 };
        System.Drawing.Rectangle work; double screenScale;
        using (var coordinates = new Native.PhysicalCoordinates())
        { var screen = PlacementService.Screen(settings); work = screen.WorkingArea; screenScale = PlacementService.Scale(screen); }
        var panel = new PanelView { DataContext = new PanelViewModel(_ => { }) };
        panel.ApplySnapshot(new AppState { Settings = settings, Tasks = [new DeadlineTask { Title = "顶部对齐检查", DeadlineUtc = DateTimeOffset.UtcNow.AddDays(2) }] });
        var panelWindow = new Window
        {
            Content = new Border { Child = panel, Margin = new Thickness(FrameWindow.SurfaceInsetDip) },
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight, ShowActivated = false, ShowInTaskbar = false
        };
        panelWindow.Show(); PlacementService.Panel(panelWindow, panel, settings, IntPtr.Zero); await Task.Delay(80);
        var positions = new List<object>();
        foreach (var margin in new[] { 24, Math.Min(160, Math.Max(24, (int)(work.Height / screenScale) - 608)) }.Distinct())
        {
            var positioned = settings with { TopMarginDip = margin };
            PlacementService.Panel(panelWindow, panel, positioned, IntPtr.Zero);
            var input = new PlacementProbe { ShowActivated = false, ShowInTaskbar = false };
            PlacementService.Input(input, positioned); input.Show(); await Task.Delay(100);
            var panelTop = panel.PointToScreen(new Point()).Y;
            var inputTop = ((Border)input.Content).PointToScreen(new Point()).Y;
            Check(Math.Abs(panelTop - inputTop) <= 1, $"panel and tall input surface tops align in physical pixels at margin {margin} (panel={panelTop}, input={inputTop})");
            Native.Rect bounds;
            using (var coordinates = new Native.PhysicalCoordinates()) Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(input).Handle, out bounds);
            Check(bounds.Top >= work.Top && bounds.Bottom <= work.Bottom && input.Scroller.ScrollableHeight > 1000 && input.Scroller.ViewportHeight > 0,
                $"tall aligned form stays inside the work area and scrolls its content at margin {margin}");
            positions.Add(new { margin, panelTop, inputTop, bounds.Top, bounds.Bottom, workAreaBottom = work.Bottom, scale = screenScale });
            input.Close(); await input.WhenClosed;
        }
        var store = new StateStore(Path.Combine(output, "test-data", "placement-" + Guid.NewGuid().ToString("N"))); await store.LoadAsync();
        var service = new TaskService(store);
        var now = DateTimeOffset.UtcNow;
        foreach (var title in new[] { "对齐任务 A", "对齐任务 B" }) await service.SaveAsync(new DeadlineTask { Title = title, CreatedAtUtc = now, UpdatedAtUtc = now, DeadlineUtc = now.AddDays(2) }, null);
        var session = new EditorSession(store, service, editor =>
        { editor.ShowActivated = false; editor.ShowInTaskbar = false; PlacementService.Input(editor, settings); editor.Show(); }, (_, _) => { });
        PlacementService.Panel(panelWindow, panel, settings, IntPtr.Zero);
        await session.OpenAsync(store.Snapshot.Tasks[0].Id); await Task.Delay(100);
        var editor = session.Current!; var top = ((Border)editor.Content).PointToScreen(new Point()).Y;
        Check(Math.Abs(top - panel.PointToScreen(new Point()).Y) <= 1, "actual task editor and panel visible tops align after native placement");
        await session.OpenAsync(store.Snapshot.Tasks[1].Id); await Task.Delay(100);
        Check(ReferenceEquals(session.Current, editor) && Math.Abs(((Border)editor.Content).PointToScreen(new Point()).Y - top) <= 1, "switching tasks preserves the aligned editor position");
        editor.Close(); await editor.WhenClosed; panelWindow.Close();
        File.WriteAllText(Path.Combine(output, "placement.json"), JsonSerializer.Serialize(positions, new JsonSerializerOptions { WriteIndented = true }));
    }
    private sealed class PlacementProbe : FrameWindow
    {
        public PlacementProbe() : base("顶部对齐检查") => Body.Children.Add(new Border { Height = 4000 });
        public ScrollViewer Scroller => BodyScroll;
    }
    private sealed class ProbeButton : Button
    {
        public void Press(bool value) => IsPressed = value;
        public void ClickNow() => OnClick();
    }
}
