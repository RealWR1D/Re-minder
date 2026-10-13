using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace ReMinder.App.UI;

/// <summary>A detached, non-commanding visual after the core confirms completion.</summary>
internal sealed class CompletionFeedback
{
    internal string TaskId { get; }
    private readonly Canvas layer;
    private readonly Grid surface;
    private readonly Path tick;
    private bool disposed;
    internal bool IsDisposed => disposed;

    internal CompletionFeedback(string taskId, FrameworkElement row, Canvas layer, Action finished)
    {
        TaskId = taskId; this.layer = layer;
        var origin = row.TranslatePoint(new Point(), layer);
        var width = row.ActualWidth; var height = row.ActualHeight;
        var dpi = VisualTreeHelper.GetDpi(row);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(row) { Stretch = Stretch.Fill }, null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(drawing); bitmap.Freeze();
        // This surface blocks a second click at the disappearing task's old location.
        surface = new Grid { Width = width, Height = height, Background = Brushes.Transparent };
        surface.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Fill });
        var button = FindCompleteButton(row);
        var point = button?.TranslatePoint(new Point(), row) ?? new Point(8, (height - 40) / 2);
        var badge = new Grid { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(point.X + ((button?.ActualWidth ?? 40) - 22) / 2, point.Y + ((button?.ActualHeight ?? 40) - 22) / 2, 0, 0) };
        var fill = new Ellipse(); fill.SetResourceReference(Shape.FillProperty, "Primary"); badge.Children.Add(fill);
        tick = new Path { Data = Geometry.Parse("M 5,11 L 9,15 L 17,7"), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeDashArray = new DoubleCollection { 10, 10 }, StrokeDashOffset = 10 };
        tick.SetResourceReference(Shape.StrokeProperty, "OnPrimary"); badge.Children.Add(tick); surface.Children.Add(badge);
        Canvas.SetLeft(surface, origin.X); Canvas.SetTop(surface, origin.Y); layer.Children.Add(surface);
        System.Windows.Automation.AutomationProperties.SetName(surface, "任务已完成");
        Motion.Animate(tick, Shape.StrokeDashOffsetProperty, 0, Motion.Check);
        Motion.Animate(Motion.Offset(surface), TranslateTransform.YProperty, -4, Motion.CompleteExit, hold: Motion.Check);
        Motion.Animate(surface, UIElement.OpacityProperty, 0, Motion.CompleteExit, () => { Dispose(); finished(); }, hold: Motion.Check);
    }

    internal void Dispose()
    {
        if (disposed) return; disposed = true;
        Motion.Stop(tick); Motion.Stop(surface);
        if (surface.RenderTransform is TranslateTransform offset) Motion.Stop(offset);
        layer.Children.Remove(surface);
    }

    private static Button? FindCompleteButton(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button { Name: "CompleteTaskButton" } button) return button;
            if (FindCompleteButton(child) is { } found) return found;
        }
        return null;
    }
}
