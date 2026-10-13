using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ReMinder.App.UI;

/// <summary>Wheel motion in pixels; direct manipulation always takes priority.</summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(Controller), typeof(SmoothScroll));
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached("Offset", typeof(double), typeof(SmoothScroll), new PropertyMetadata(0d, OffsetChanged));
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    private static void OffsetChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is ScrollViewer viewer && viewer.GetValue(StateProperty) is Controller { Running: true }) viewer.ScrollToVerticalOffset((double)args.NewValue);
    }
    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ScrollViewer viewer) return;
        if ((bool)args.NewValue)
        {
            var state = new Controller(viewer); viewer.SetValue(StateProperty, state);
            viewer.Loaded += state.Loaded; viewer.Unloaded += state.Unloaded;
            if (viewer.IsLoaded) state.Loaded(viewer, new RoutedEventArgs());
        }
        else if (viewer.GetValue(StateProperty) is Controller state)
        { state.Unloaded(viewer, new RoutedEventArgs()); viewer.Loaded -= state.Loaded; viewer.Unloaded -= state.Unloaded; viewer.ClearValue(StateProperty); }
    }
    public static void Cancel(ScrollViewer viewer)
    {
        if (viewer.GetValue(StateProperty) is Controller state) state.Cancel();
    }
    internal static bool Move(ScrollViewer viewer, double pixels) => viewer.GetValue(StateProperty) is Controller state && state.Move(pixels);
    internal static int Duration(double distance) => (int)Math.Clamp(160 + Math.Abs(distance) * .35, 160, 280);
    private sealed class Controller(ScrollViewer viewer)
    {
        internal bool Running;
        private bool attached;
        private double target;
        private int direction;
        public void Loaded(object sender, RoutedEventArgs args)
        {
            if (attached) return; attached = true;
            viewer.PreviewMouseWheel += Wheel; viewer.PreviewMouseDown += MouseDown; viewer.PreviewKeyDown += KeyDown;
            viewer.ScrollChanged += SizeChanged;
            viewer.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(DragStarted), true);
            viewer.AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(BringIntoView), true);
        }
        public void Unloaded(object sender, RoutedEventArgs args)
        {
            if (!attached) return; attached = false; Cancel();
            viewer.PreviewMouseWheel -= Wheel; viewer.PreviewMouseDown -= MouseDown; viewer.PreviewKeyDown -= KeyDown; viewer.ScrollChanged -= SizeChanged;
            viewer.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(DragStarted));
            viewer.RemoveHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(BringIntoView));
        }
        internal bool Move(double pixels)
        {
            if (viewer.ScrollableHeight <= 0 || pixels == 0) return false;
            var nextDirection = Math.Sign(pixels);
            var current = Running ? (double)viewer.GetValue(OffsetProperty) : viewer.VerticalOffset;
            var origin = Running && direction == nextDirection ? target : current;
            target = Math.Clamp(origin + pixels, 0, viewer.ScrollableHeight); direction = nextDirection;
            if (!Motion.Enabled) { var destination = target; Cancel(); viewer.ScrollToVerticalOffset(destination); return true; }
            if (Math.Abs(target - current) < .01) return false;
            if (!Running) viewer.SetValue(OffsetProperty, current);
            Running = true;
            Motion.Animate(viewer, OffsetProperty, target, Duration(target - current), () => Running = false);
            return true;
        }
        internal void Cancel()
        {
            Running = false; Motion.Stop(viewer);
            target = viewer.VerticalOffset; direction = 0; viewer.SetValue(OffsetProperty, target);
        }
        private void Wheel(object sender, MouseWheelEventArgs args)
        {
            if (args.Handled || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || SystemParameters.WheelScrollLines == 0) return;
            // Nested notes/menus should receive their own wheel input before the containing form.
            var owner = args.OriginalSource as DependencyObject;
            while (owner is not null)
            {
                if (owner is ScrollViewer nested && nested.ScrollableHeight > 0) { if (!ReferenceEquals(nested, viewer)) return; break; }
                owner = owner is Visual ? VisualTreeHelper.GetParent(owner) : LogicalTreeHelper.GetParent(owner);
            }
            var step = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight * .9 : SystemParameters.WheelScrollLines * 24d;
            if (Move(-args.Delta / 120d * step)) args.Handled = true;
        }
        private void MouseDown(object sender, MouseButtonEventArgs args) => Cancel();
        private void DragStarted(object sender, DragStartedEventArgs args) => Cancel();
        private void BringIntoView(object sender, RequestBringIntoViewEventArgs args) => Cancel();
        private void KeyDown(object sender, KeyEventArgs args) { if (args.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End) Cancel(); }
        private void SizeChanged(object sender, ScrollChangedEventArgs args)
        { if (Running && (viewer.ScrollableHeight <= 0 || args.ViewportHeightChange != 0)) Cancel(); }
    }
}
