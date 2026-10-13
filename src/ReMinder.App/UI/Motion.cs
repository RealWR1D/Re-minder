using System.ComponentModel;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ReMinder.App.UI;

/// <summary>Interruptible UI motion. Commands never wait for an animation.</summary>
public static class Motion
{
    public const int DurationPercent = 60;
    public const int Press = 160 * DurationPercent / 100, Hover = 200 * DurationPercent / 100, Release = 360 * DurationPercent / 100,
        Enter = 440 * DurationPercent / 100, Exit = 320 * DurationPercent / 100, Content = 320 * DurationPercent / 100,
        Scroll = 480 * DurationPercent / 100, RippleExpansion = 640 * DurationPercent / 100, RippleFade = 500 * DurationPercent / 100,
        RippleHold = 220 * DurationPercent / 100, Check = 200 * DurationPercent / 100,
        Focus = 300 * DurationPercent / 100, Blur = 240 * DurationPercent / 100;
    public const double PressScale = .985;
    public const int CalendarPage = 180, EditorReplace = 150, CompleteExit = 160;

    public static void RefreshContent(FrameworkElement element)
    {
        if (!Enabled) { Stop(element); element.Opacity = 1; return; }
        if (!element.HasAnimatedProperties) element.Opacity = .72;
        Animate(element, UIElement.OpacityProperty, 1, EditorReplace);
    }
    private static readonly Dictionary<(DependencyObject Target, DependencyProperty Property), Action> active = new();
    internal static event Action? PreferenceChanged;
    public static bool ShuttingDown { get; set; }
    public static bool Enabled => !Theme.Reduced && !ShuttingDown;

    internal static void UpdatePreference()
    {
        if (!Enabled) foreach (var finish in active.Values.ToArray()) finish();
        PreferenceChanged?.Invoke();
    }

    public static void Animate(DependencyObject target, DependencyProperty property, double destination, int milliseconds, Action? completed = null, int hold = 0)
    {
        var key = (target, property);
        var start = (double)target.GetValue(property);
        active.Remove(key);
        SetAnimation(target, property, null);
        // These are motion-owned properties. Set their base value, so FillBehavior.Stop
        // cannot restore the pre-animation value when WPF invalidates a current value.
        target.SetValue(property, destination);
        if (!Enabled || Math.Abs(start - destination) < .0001) { completed?.Invoke(); return; }
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (hold > 0) animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(hold))));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(destination, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds + hold)), new KeySpline(.2, 0, .2, 1)));
        Action finish = null!;
        finish = () =>
        {
            if (!active.TryGetValue(key, out var current) || !ReferenceEquals(current, finish)) return;
            active.Remove(key); SetAnimation(target, property, null); completed?.Invoke();
        };
        active[key] = finish;
        animation.Completed += (_, _) => finish();
        SetAnimation(target, property, animation);
    }

    private static void SetAnimation(DependencyObject target, DependencyProperty property, AnimationTimeline? animation)
    {
        if (target is UIElement element) element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        else if (target is Animatable value) value.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Stop(DependencyObject target)
    {
        foreach (var key in active.Keys.Where(k => ReferenceEquals(k.Target, target)).ToArray())
        { active.Remove(key); SetAnimation(target, key.Property, null); }
    }

    public static void Appear(FrameworkElement element, int milliseconds = Enter, double distance = 10)
    {
        if (!Enabled) { Stop(element); element.SetCurrentValue(UIElement.OpacityProperty, 1d); if (element.RenderTransform is TranslateTransform existing) { Stop(existing); existing.Y = 0; } return; }
        var offset = Offset(element);
        Stop(element); Stop(offset); element.Opacity = 0; offset.Y = distance;
        Animate(element, UIElement.OpacityProperty, 1, milliseconds);
        Animate(offset, TranslateTransform.YProperty, 0, milliseconds);
    }

    public static void Disappear(FrameworkElement element, Action completed)
    {
        var offset = Offset(element);
        Animate(offset, TranslateTransform.YProperty, -4, Exit);
        Animate(element, UIElement.OpacityProperty, 0, Exit, completed);
    }

    public static TranslateTransform Offset(FrameworkElement element)
    {
        if (element.RenderTransform is not TranslateTransform offset) element.RenderTransform = offset = new TranslateTransform();
        return offset;
    }

    public static readonly DependencyProperty EnterOnLoadProperty = DependencyProperty.RegisterAttached("EnterOnLoad", typeof(bool), typeof(Motion), new PropertyMetadata(false, EnterChanged));
    public static void SetEnterOnLoad(DependencyObject target, bool value) => target.SetValue(EnterOnLoadProperty, value);
    public static bool GetEnterOnLoad(DependencyObject target) => (bool)target.GetValue(EnterOnLoadProperty);
    private static void EnterChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        if ((bool)args.NewValue) { element.Loaded += EnterLoaded; element.Unloaded += EnterUnloaded; }
        else { element.Loaded -= EnterLoaded; element.Unloaded -= EnterUnloaded; }
    }
    private static void EnterLoaded(object sender, RoutedEventArgs args) => Appear((FrameworkElement)sender, Content, 4);
    private static void EnterUnloaded(object sender, RoutedEventArgs args)
    { var element = (FrameworkElement)sender; Stop(element); if (element.RenderTransform is TranslateTransform offset) Stop(offset); }

    public static readonly DependencyProperty RevealProperty = DependencyProperty.RegisterAttached("Reveal", typeof(bool), typeof(Motion), new PropertyMetadata(true, RevealChanged));
    public static void SetReveal(DependencyObject target, bool value) => target.SetValue(RevealProperty, value);
    public static bool GetReveal(DependencyObject target) => (bool)target.GetValue(RevealProperty);
    private static void RevealChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.Unloaded -= RevealUnloaded; element.Unloaded += RevealUnloaded;
        var visible = (bool)args.NewValue;
        if (!element.IsLoaded || !Enabled)
        { Stop(element); element.Height = double.NaN; element.Opacity = 1; element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; element.IsHitTestVisible = visible; return; }
        var start = element.Visibility == Visibility.Visible ? element.ActualHeight : 0;
        var opacity = element.Visibility == Visibility.Visible ? element.Opacity : 0;
        element.Visibility = Visibility.Visible; element.IsHitTestVisible = visible; element.ClipToBounds = true;
        // Measure the natural size once, then animate only this disclosure region.
        Stop(element); element.Height = double.NaN;
        var availableWidth = element.ActualWidth > 0 ? element.ActualWidth + element.Margin.Left + element.Margin.Right : (element.Parent as FrameworkElement)?.ActualWidth ?? 0;
        if (element.ActualWidth <= 0 && element.Parent is Border parentBorder) availableWidth -= parentBorder.Padding.Left + parentBorder.Padding.Right;
        element.Measure(new Size(availableWidth > 0 ? availableWidth : double.PositiveInfinity, double.PositiveInfinity));
        var end = visible ? element.DesiredSize.Height - element.Margin.Top - element.Margin.Bottom : 0;
        element.Height = start; element.Opacity = opacity;
        Animate(element, UIElement.OpacityProperty, visible ? 1 : 0, Release);
        Animate(element, FrameworkElement.HeightProperty, Math.Max(0, end), Enter, () =>
        {
            element.Height = double.NaN; element.Visibility = GetReveal(element) ? Visibility.Visible : Visibility.Collapsed;
            element.Opacity = 1; element.ClipToBounds = false;
        });
    }
    private static void RevealUnloaded(object sender, RoutedEventArgs args)
    {
        var element = (FrameworkElement)sender;
        Stop(element); element.Height = double.NaN; element.Opacity = 1; element.ClipToBounds = false;
        element.Visibility = GetReveal(element) ? Visibility.Visible : Visibility.Collapsed;
    }

    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.RegisterAttached("Interactive", typeof(bool), typeof(Motion), new PropertyMetadata(false, InteractiveChanged));
    private static readonly DependencyProperty FeedbackProperty = DependencyProperty.RegisterAttached("Feedback", typeof(Feedback), typeof(Motion));
    public static void SetInteractive(DependencyObject target, bool value) => target.SetValue(InteractiveProperty, value);
    public static bool GetInteractive(DependencyObject target) => (bool)target.GetValue(InteractiveProperty);
    private static void InteractiveChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ButtonBase button) return;
        if ((bool)args.NewValue)
        {
            var feedback = new Feedback(button); button.SetValue(FeedbackProperty, feedback);
            button.Loaded += feedback.Loaded; button.Unloaded += feedback.Unloaded;
            if (button.IsLoaded) feedback.Loaded(button, new RoutedEventArgs());
        }
        else if (button.GetValue(FeedbackProperty) is Feedback feedback)
        { button.Loaded -= feedback.Loaded; button.Unloaded -= feedback.Unloaded; feedback.Unloaded(button, new RoutedEventArgs()); button.ClearValue(FeedbackProperty); }
    }

    private sealed class Feedback(ButtonBase button)
    {
        private static readonly DependencyPropertyDescriptor pressed = DependencyPropertyDescriptor.FromProperty(ButtonBase.IsPressedProperty, typeof(ButtonBase));
        private static readonly DependencyPropertyDescriptor hovered = DependencyPropertyDescriptor.FromProperty(UIElement.IsMouseOverProperty, typeof(ButtonBase));
        private FrameworkElement? surface, state, tick;
        private FrameworkElement? checkFill;
        private Canvas? rippleHost;
        private ScaleTransform? scale;
        private Ellipse? ripple;
        private readonly System.Diagnostics.Stopwatch rippleAge = new();
        private bool attached, wasPressed;
        private bool? wasChecked;
        private Point? pointer;

        public void Loaded(object sender, RoutedEventArgs args)
        {
            if (attached) return;
            button.ApplyTemplate(); surface = button.Template.FindName("Surface", button) as FrameworkElement;
            state = button.Template.FindName("State", button) as FrameworkElement;
            tick = button.Template.FindName("Tick", button) as FrameworkElement;
            checkFill = button.Template.FindName("CheckFill", button) as FrameworkElement;
            rippleHost = button.Template.FindName("RippleHost", button) as Canvas;
            scale = surface?.RenderTransform as ScaleTransform;
            // WPF freezes transforms in shared control templates; each control needs a writable instance.
            if (scale is { IsFrozen: true } && surface is not null) { scale = scale.Clone(); surface.RenderTransform = scale; }
            attached = true;
            pressed.AddValueChanged(button, Changed); hovered.AddValueChanged(button, Changed);
            button.IsEnabledChanged += EnabledChanged; button.PreviewMouseLeftButtonDown += PointerDown; button.PreviewKeyDown += KeyDown;
            if (button is ToggleButton toggle) { toggle.Checked += Checked; toggle.Unchecked += Checked; }
            PreferenceChanged += PolicyChanged;
            SetCheck(false);
            Changed(null, EventArgs.Empty);
        }
        public void Unloaded(object sender, RoutedEventArgs args)
        {
            if (!attached) return;
            attached = false; pressed.RemoveValueChanged(button, Changed); hovered.RemoveValueChanged(button, Changed);
            button.IsEnabledChanged -= EnabledChanged; button.PreviewMouseLeftButtonDown -= PointerDown; button.PreviewKeyDown -= KeyDown;
            if (button is ToggleButton toggle) { toggle.Checked -= Checked; toggle.Unchecked -= Checked; }
            PreferenceChanged -= PolicyChanged;
            if (surface is not null) Stop(surface);
            if (state is not null) Stop(state);
            if (tick is not null) Stop(tick);
            if (checkFill is not null) Stop(checkFill);
            if (scale is not null) { Stop(scale); scale.ScaleX = scale.ScaleY = 1; }
            ClearRipple(); wasPressed = false; pointer = null;
        }
        private void PointerDown(object sender, MouseButtonEventArgs args) => pointer = surface is null ? null : args.GetPosition(surface);
        private void KeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key is Key.Space or Key.Enter) pointer = null;
            // WPF invokes an Enter-key button without setting IsPressed.
            if (args.Key != Key.Enter || args.IsRepeat || !button.IsEnabled) return;
            Ripple(); FadeRipple();
            if (scale is null || !Enabled) return;
            Animate(scale, ScaleTransform.ScaleXProperty, PressScale, Press, () => { if (!button.IsPressed) Animate(scale, ScaleTransform.ScaleXProperty, 1, Release); });
            Animate(scale, ScaleTransform.ScaleYProperty, PressScale, Press, () => { if (!button.IsPressed) Animate(scale, ScaleTransform.ScaleYProperty, 1, Release); });
        }
        private void EnabledChanged(object sender, DependencyPropertyChangedEventArgs args) => Changed(null, EventArgs.Empty);
        private void Checked(object sender, RoutedEventArgs args) => SetCheck(true);
        private void SetCheck(bool animate)
        {
            var selected = button is ToggleButton { IsChecked: true };
            if (animate && wasChecked == selected) return;
            wasChecked = selected;
            void Set(DependencyObject target, DependencyProperty property, double value)
            { if (animate) Animate(target, property, value, Check); else { Stop(target); target.SetValue(property, value); } }
            if (tick is not null) Set(tick, UIElement.OpacityProperty, selected ? 1 : 0);
            // Dash lengths are in stroke-width units; the complete tick is about 8.2 units long.
            if (tick is System.Windows.Shapes.Path path) Set(path, Shape.StrokeDashOffsetProperty, selected ? 0 : 10);
            if (checkFill is not null) Set(checkFill, UIElement.OpacityProperty, selected ? 1 : 0);
        }
        private void PolicyChanged() { ClearRipple(); Changed(null, EventArgs.Empty); }
        private void Changed(object? sender, EventArgs args)
        {
            var down = button.IsEnabled && button.IsPressed;
            if (state is not null) Animate(state, UIElement.OpacityProperty, !button.IsEnabled ? 0 : down ? .12 : button.IsMouseOver ? .08 : 0, down ? Press : Hover);
            if (scale is not null)
            { Animate(scale, ScaleTransform.ScaleXProperty, down ? PressScale : 1, down ? Press : Release); Animate(scale, ScaleTransform.ScaleYProperty, down ? PressScale : 1, down ? Press : Release); }
            if (down && !wasPressed) Ripple();
            if (!down && wasPressed) FadeRipple();
            wasPressed = down;
            if (!button.IsEnabled) ClearRipple();
            if (!Enabled && scale is not null) scale.ScaleX = scale.ScaleY = 1;
        }
        private void ClearRipple()
        {
            if (rippleHost is not null) foreach (var ink in rippleHost.Children.OfType<Ellipse>()) { Stop(ink); if (ink.RenderTransform is ScaleTransform transform) Stop(transform); }
            rippleHost?.Children.Clear(); ripple = null;
        }
        private void Ripple()
        {
            if (!Enabled || rippleHost is null || surface is null) return;
            // Allow successive waves to fade naturally, with a bounded visual count.
            if (rippleHost.Children.Count >= 6 && rippleHost.Children[0] is Ellipse oldest) { Stop(oldest); Stop(oldest.RenderTransform); rippleHost.Children.Remove(oldest); }
            var w = surface.ActualWidth; var h = surface.ActualHeight;
            var point = pointer ?? new Point(w / 2, h / 2);
            var radius = Math.Sqrt(Math.Pow(Math.Max(point.X, w - point.X), 2) + Math.Pow(Math.Max(point.Y, h - point.Y), 2));
            if (radius <= 0) return;
            rippleHost.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 20, 20);
            var growth = new ScaleTransform(.02, .02);
            var ink = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = button.Foreground, Opacity = .13, RenderTransformOrigin = new Point(.5, .5), RenderTransform = growth, IsHitTestVisible = false };
            Canvas.SetLeft(ink, point.X - radius); Canvas.SetTop(ink, point.Y - radius);
            ripple = ink; rippleAge.Restart(); rippleHost.Children.Add(ink);
            Animate(growth, ScaleTransform.ScaleXProperty, 1, RippleExpansion); Animate(growth, ScaleTransform.ScaleYProperty, 1, RippleExpansion);
        }
        private void FadeRipple()
        {
            if (ripple is not { } ink || rippleHost is null) return;
            var hold = (int)Math.Max(0, RippleHold - rippleAge.ElapsedMilliseconds);
            Animate(ink, UIElement.OpacityProperty, 0, RippleFade, () => { Stop(ink.RenderTransform); rippleHost.Children.Remove(ink); if (ReferenceEquals(ripple, ink)) ripple = null; }, hold);
        }
    }
}
