namespace ReMinder.App.UI;

/// <summary>Focus feedback on visual overlays, leaving text layout and selection stable.</summary>
public static class TextFieldMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(TextFieldMotion), new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty FeedbackProperty = DependencyProperty.RegisterAttached("Feedback", typeof(Feedback), typeof(TextFieldMotion));
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Control field) return;
        if ((bool)args.NewValue)
        {
            var feedback = new Feedback(field); field.SetValue(FeedbackProperty, feedback);
            field.Loaded += feedback.Loaded; field.Unloaded += feedback.Unloaded;
            if (field.IsLoaded) feedback.Loaded(field, new RoutedEventArgs());
        }
        else if (field.GetValue(FeedbackProperty) is Feedback feedback)
        {
            field.Loaded -= feedback.Loaded; field.Unloaded -= feedback.Unloaded;
            feedback.Unloaded(field, new RoutedEventArgs()); field.ClearValue(FeedbackProperty);
        }
    }

    private sealed class Feedback(Control field)
    {
        private FrameworkElement? outline, tint;
        private bool attached;
        public void Loaded(object sender, RoutedEventArgs args)
        {
            if (attached) return;
            field.ApplyTemplate();
            outline = field.Template.FindName("FocusOutline", field) as FrameworkElement;
            tint = field.Template.FindName("FocusTint", field) as FrameworkElement;
            attached = true;
            field.IsKeyboardFocusWithinChanged += Changed;
            field.IsEnabledChanged += Changed;
            if (field is DatePicker date) { date.CalendarOpened += CalendarChanged; date.CalendarClosed += CalendarChanged; }
            Motion.PreferenceChanged += PolicyChanged;
            Update();
        }
        public void Unloaded(object sender, RoutedEventArgs args)
        {
            if (!attached) return;
            attached = false;
            field.IsKeyboardFocusWithinChanged -= Changed;
            field.IsEnabledChanged -= Changed;
            if (field is DatePicker date) { date.CalendarOpened -= CalendarChanged; date.CalendarClosed -= CalendarChanged; }
            Motion.PreferenceChanged -= PolicyChanged;
            if (outline is not null) { Motion.Stop(outline); outline.Opacity = 0; }
            if (tint is not null) { Motion.Stop(tint); tint.Opacity = 0; }
        }
        private void Changed(object sender, DependencyPropertyChangedEventArgs args) => Update();
        private void CalendarChanged(object? sender, RoutedEventArgs args) => Update();
        private void PolicyChanged() => Update();
        private void Update()
        {
            var focused = field.IsEnabled && (field.IsKeyboardFocusWithin || field is DatePicker { IsDropDownOpen: true });
            var duration = focused ? Motion.Focus : Motion.Blur;
            if (outline is not null) Motion.Animate(outline, UIElement.OpacityProperty, focused ? 1 : 0, duration);
            if (tint is not null) Motion.Animate(tint, UIElement.OpacityProperty, focused ? .035 : 0, duration);
        }
    }
}
