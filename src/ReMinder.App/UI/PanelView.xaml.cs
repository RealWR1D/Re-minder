using System.Windows.Input;
using System.Windows.Threading;

namespace ReMinder.App.UI;

public partial class PanelView : UserControl
{
    private readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly System.Diagnostics.Stopwatch elapsed = new();
    private TimeSpan remaining;
    public PanelView()
    {
        InitializeComponent();
        toastTimer.Tick += (_, _) => { if (!Toast.IsMouseOver && !Toast.IsKeyboardFocusWithin) { Toast.Visibility = Visibility.Collapsed; toastTimer.Stop(); elapsed.Reset(); } else Pause(); };
        Toast.MouseEnter += (_, _) => Pause(); Toast.GotKeyboardFocus += (_, _) => Pause();
        Toast.MouseLeave += (_, _) => Resume(); Toast.LostKeyboardFocus += (_, _) => Resume();
        DismissToast.Click += (_, _) => { Toast.Visibility = Visibility.Collapsed; toastTimer.Stop(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.N && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && DataContext is PanelViewModel model) { model.New.Execute(null); e.Handled = true; } };
        Unloaded += (_, _) => toastTimer.Stop();
    }
    public void ShowToast(string text, bool canUndo)
    {
        ToastText.Text = text; UndoButton.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed; Toast.Visibility = Visibility.Visible;
        toastTimer.Stop(); elapsed.Reset(); remaining = TimeSpan.FromSeconds(8); Resume();
        System.Windows.Automation.AutomationProperties.SetName(Toast, text);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(Toast, System.Windows.Automation.AutomationLiveSetting.Polite);
    }
    private void Pause() { if (!toastTimer.IsEnabled) return; remaining -= elapsed.Elapsed; toastTimer.Stop(); elapsed.Reset(); }
    private void Resume() { if (Toast.Visibility != Visibility.Visible || Toast.IsMouseOver || Toast.IsKeyboardFocusWithin) return; toastTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, remaining.TotalMilliseconds)); elapsed.Restart(); toastTimer.Start(); }
    public void FocusTask(string id)
    {
        if (DataContext is not PanelViewModel model) return;
        var item = model.Tasks.FirstOrDefault(x => x.Task.Id == id); if (item is null) return;
        TaskList.ScrollIntoView(item);
    }
}
