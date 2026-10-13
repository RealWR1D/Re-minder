using System.Windows.Documents;

namespace ReMinder.App.UI;

public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(Icon), new FrameworkPropertyMetadata("clock", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(Icon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    static Icon()
    {
        WidthProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(20d));
        HeightProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(20d));
    }
    public Icon() { Focusable = false; IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var pen = new Pen(Foreground, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        var data = Kind switch
        {
            "plus" => "M12,5 L12,19 M5,12 L19,12",
            "close" => "M6,6 L18,18 M18,6 L6,18",
            "check" => "M5,12 L10,17 L19,7",
            "arrow" => "M7,17 L17,7 M7,7 L17,7 L17,17",
            "down" => "M6,9 L12,15 L18,9",
            "up" => "M6,15 L12,9 L18,15",
            "left" => "M15,6 L9,12 L15,18",
            "right" => "M9,6 L15,12 L9,18",
            "restore" => "M4,8 L4,3 M4,8 L9,8 M4,8 A8,8 0 1 1 5,18",
            "trash" => "M4,6 L20,6 M9,6 L9,3 L15,3 L15,6 M6,6 L7,21 L17,21 L18,6 M10,10 L10,17 M14,10 L14,17",
            "calendar" => "M5,5 L19,5 Q21,5 21,7 L21,19 Q21,21 19,21 L5,21 Q3,21 3,19 L3,7 Q3,5 5,5 M3,10 L21,10 M7,3 L7,7 M17,3 L17,7",
            "warning" => "M12,3 A9,9 0 1 1 11.99,3 M12,7 L12,13 M12,17 L12,17.1",
            "circle" => "M12,3 A9,9 0 1 1 11.99,3",
            "bell" => "M5,16 L7,13 L7,8 A5,5 0 0 1 17,8 L17,13 L19,16 Z M10,20 Q12,23 14,20",
            "pin" => "M8,3 L16,3 L15,9 L18,13 L6,13 L9,9 Z M12,13 L12,21",
            "export" => "M12,15 L12,3 M8,7 L12,3 L16,7 M5,13 L5,20 L19,20 L19,13",
            "import" => "M12,3 L12,15 M8,11 L12,15 L16,11 M5,13 L5,20 L19,20 L19,13",
            "settings" => "M5,6 L19,6 M5,12 L19,12 M5,18 L19,18 M9,3 L9,9 M15,9 L15,15 M10,15 L10,21",
            _ => "M12,3 A9,9 0 1 1 11.99,3 M12,7 L12,12 L16,14"
        };
        if (Kind == "more") { for (var x = 5; x <= 19; x += 7) dc.DrawEllipse(Foreground, null, new Point(x, 12), 1.2, 1.2); }
        else dc.DrawGeometry(null, pen, Geometry.Parse(data));
        dc.Pop();
    }
}
