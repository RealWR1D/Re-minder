using System.Windows.Input;
using System.Windows.Media.Effects;

namespace ReMinder.App.UI;

public class FrameWindow : Window
{
    protected readonly StackPanel Body = new();
    protected readonly Grid Layout = new();
    protected readonly TextBlock Heading = new();
    protected readonly Button CloseButton;
    public FrameWindow(string title, double width = 420)
    {
        Title = $"{title} · Re:minder"; Width = width + 24; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 40;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true;
        var border = new Border { CornerRadius = new CornerRadius(28), Padding = new Thickness(20), Margin = new Thickness(12), Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = .12 } };
        border.SetResourceReference(Border.BackgroundProperty, "Surface");
        Layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Layout.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Heading.Text = title; Heading.SetResourceReference(TextBlock.FontSizeProperty, "HeadingSize"); Heading.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(Heading);
        CloseButton = Controls.IconButton("close", "关闭窗口", () => Close()); Grid.SetColumn(CloseButton, 1); header.Children.Add(CloseButton);
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock) DragMove(); };
        Layout.Children.Add(header);
        var scroll = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(2), Margin = new Thickness(-2) };
        Grid.SetRow(scroll, 1); Layout.Children.Add(scroll); border.Child = Layout; Content = border;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
}

public static class Controls
{
    public static TextBlock Text(string text, string? style = null) { var t = new TextBlock { Text = text }; if (style is not null) t.SetResourceReference(FrameworkElement.StyleProperty, style); return t; }
    public static Icon Glyph(string kind, string color = "Muted") { var icon = new Icon { Kind = kind }; icon.SetResourceReference(Icon.ForegroundProperty, color); return icon; }
    public static Button Button(string text, Action action, string? style = null, string? icon = null)
    {
        var b = new Button(); if (style is not null) b.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (icon is not null) { var row = new StackPanel { Orientation = Orientation.Horizontal }; var i = new Icon { Kind = icon, Margin = new Thickness(0, 0, 6, 0) }; i.SetBinding(Icon.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = b }); row.Children.Add(i); var label = Text(text); label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = b }); row.Children.Add(label); b.Content = row; }
        else { var label = Text(text); label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = b }); b.Content = label; }
        System.Windows.Automation.AutomationProperties.SetName(b, text); b.Click += (_, _) => action(); return b;
    }
    public static Button IconButton(string kind, string name, Action action)
    {
        var b = Button("", action, "IconButton"); b.Content = Glyph(kind); b.ToolTip = name; System.Windows.Automation.AutomationProperties.SetName(b, name); return b;
    }
    public static TextBlock Label(string text) { var t = Text(text, "Small"); t.Margin = new Thickness(2, 16, 0, 6); return t; }
    public static Border Card(UIElement child, string color = "Card", int radius = 16) { var b = new Border { Child = child, CornerRadius = new CornerRadius(radius), Padding = new Thickness(12) }; b.SetResourceReference(Border.BackgroundProperty, color); return b; }
    public static async void Safely(Func<Task> work, Action<string>? error = null)
    {
        try { await work(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        { if (error is not null) error(e.Message); else MessageBox.Show(e.Message, "Re:minder", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
