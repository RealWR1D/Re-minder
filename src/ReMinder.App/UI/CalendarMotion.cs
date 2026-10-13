using System.Windows.Controls.Primitives;
using System.ComponentModel;

namespace ReMinder.App.UI;

/// <summary>Animate the native calendar's visual states without replacing its date/keyboard logic.</summary>
public static class CalendarMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(CalendarMotion), new PropertyMetadata(false, Changed));
    private static readonly DependencyProperty FeedbackProperty = DependencyProperty.RegisterAttached("Feedback", typeof(Feedback), typeof(CalendarMotion));
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Control control) return;
        if ((bool)args.NewValue)
        {
            var feedback = new Feedback(control); control.SetValue(FeedbackProperty, feedback);
            control.Loaded += feedback.Loaded; control.Unloaded += feedback.Unloaded;
            if (control.IsLoaded) feedback.Loaded(control, new RoutedEventArgs());
        }
        else if (control.GetValue(FeedbackProperty) is Feedback feedback)
        {
            control.Loaded -= feedback.Loaded; control.Unloaded -= feedback.Unloaded;
            feedback.Unloaded(control, new RoutedEventArgs()); control.ClearValue(FeedbackProperty);
        }
    }
    private sealed class Feedback(Control control)
    {
        private readonly List<DependencyPropertyDescriptor> properties = [];
        private readonly Dictionary<string, FrameworkElement> parts = [];
        private FrameworkElement? view;
        private bool attached;
        public void Loaded(object sender, RoutedEventArgs args)
        {
            if (attached) return;
            attached = true; control.ApplyTemplate();
            if (control is Calendar calendar)
            { calendar.DisplayDateChanged += DateChanged; calendar.DisplayModeChanged += ModeChanged; }
            else
            {
                foreach (var name in new[] { "SelectedFill", "SelectedText", "NormalText", "TodayOutline", "FocusHalo", "DayContent" })
                    if (control.Template.FindName(name, control) is FrameworkElement element) parts[name] = element;
                // CalendarItem recycles cells. Empty VisualStates can retain their old state
                // until pointer input; the native dependency properties are the source of truth.
                var watched = control is CalendarDayButton
                    ? new[] { CalendarDayButton.IsSelectedProperty, CalendarDayButton.IsTodayProperty, CalendarDayButton.IsInactiveProperty, CalendarDayButton.IsBlackedOutProperty }
                    : new[] { CalendarButton.HasSelectedDaysProperty, CalendarButton.IsInactiveProperty };
                foreach (var property in watched)
                {
                    var descriptor = DependencyPropertyDescriptor.FromProperty(property, control.GetType());
                    properties.Add(descriptor); descriptor.AddValueChanged(control, StateChanged);
                }
                control.IsKeyboardFocusWithinChanged += FocusChanged;
                Update(false);
            }
            Motion.PreferenceChanged += PolicyChanged;
        }
        public void Unloaded(object sender, RoutedEventArgs args)
        {
            if (!attached) return;
            attached = false; Motion.PreferenceChanged -= PolicyChanged;
            foreach (var property in properties) property.RemoveValueChanged(control, StateChanged);
            control.IsKeyboardFocusWithinChanged -= FocusChanged;
            foreach (var part in parts.Values) Motion.Stop(part);
            properties.Clear(); parts.Clear();
            if (control is Calendar calendar)
            { calendar.DisplayDateChanged -= DateChanged; calendar.DisplayModeChanged -= ModeChanged; }
            StopView();
        }
        private void StateChanged(object? sender, EventArgs args) => Update(true);
        private void FocusChanged(object sender, DependencyPropertyChangedEventArgs args) => Update(true);
        private void PolicyChanged() { Update(true); if (!Motion.Enabled) StopView(); }
        private void Update(bool animate)
        {
            void Opacity(string name, double value)
            {
                if (!parts.TryGetValue(name, out var part)) return;
                if (animate) Motion.Animate(part, UIElement.OpacityProperty, value, Motion.Check);
                else { Motion.Stop(part); part.Opacity = value; }
            }
            var selected = control is CalendarDayButton { IsSelected: true } or CalendarButton { HasSelectedDays: true };
            Opacity("SelectedFill", selected ? 1 : 0);
            Opacity("SelectedText", selected ? 1 : 0);
            Opacity("NormalText", selected ? 0 : 1);
            Opacity("TodayOutline", control is CalendarDayButton { IsToday: true } ? 1 : 0);
            Opacity("FocusHalo", control.IsKeyboardFocusWithin ? .12 : 0);
            var inactive = control is CalendarDayButton { IsInactive: true } or CalendarButton { IsInactive: true };
            Opacity("DayContent", control is CalendarDayButton { IsBlackedOut: true } ? .28 : inactive && !selected ? .45 : 1);
        }
        private void DateChanged(object? sender, CalendarDateChangedEventArgs args)
        {
            if (args.AddedDate?.Month != args.RemovedDate?.Month || args.AddedDate?.Year != args.RemovedDate?.Year)
                ChangeView(Math.Sign(DateTime.Compare(args.AddedDate ?? DateTime.Today, args.RemovedDate ?? DateTime.Today)));
        }
        private void ModeChanged(object? sender, CalendarModeChangedEventArgs args) => ChangeView(0);
        private void StopView()
        {
            if (view is null) return;
            Motion.Stop(view); view.Opacity = 1;
            if (view.RenderTransform is TranslateTransform offset) { Motion.Stop(offset); offset.X = offset.Y = 0; }
            view = null;
        }
        private void ChangeView(int direction)
        {
            if (control is not Calendar calendar) return;
            if (calendar.Template.FindName("PART_CalendarItem", calendar) is not CalendarItem item) return;
            item.ApplyTemplate();
            var next = item.Template.FindName(calendar.DisplayMode == CalendarMode.Month ? "PART_MonthView" : "PART_YearView", item) as FrameworkElement;
            if (!ReferenceEquals(view, next)) StopView();
            view = next;
            if (view is null) return;
            var offset = Motion.Offset(view);
            if (!Motion.Enabled) { StopView(); return; }
            // Retarget in-flight transitions rather than flashing back to zero opacity.
            if (!offset.HasAnimatedProperties) { offset.X = direction * 12; offset.Y = 0; }
            if (!view.HasAnimatedProperties) view.Opacity = .65;
            Motion.Animate(offset, TranslateTransform.XProperty, 0, Motion.CalendarPage);
            Motion.Animate(view, UIElement.OpacityProperty, 1, Motion.CalendarPage);
        }
    }
}
