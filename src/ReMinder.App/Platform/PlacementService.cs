using System.Windows.Interop;
using ReMinder.App.UI;
using Forms = System.Windows.Forms;

namespace ReMinder.App.Platform;

internal static class PlacementService
{
    // Window coordinates include the transparent shadow margin; settings describe the visible surface.
    internal static int SurfaceTop(int workTop, int topMarginDip, double scale) => Math.Max(workTop, workTop + (int)Math.Round((topMarginDip - FrameWindow.SurfaceInsetDip) * scale));
    public static Forms.Screen Screen(AppSettings settings) => Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.MonitorPreference) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    public static double Scale(Forms.Screen screen)
    {
        var handle = Native.MonitorFromPoint(new Native.Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2), 2);
        return Native.GetDpiForMonitor(handle, 0, out var x, out _) == 0 ? x / 96d : 1;
    }
    public static void Panel(Window window, FrameworkElement content, AppSettings settings, IntPtr parent)
    {
        using var physical = new Native.PhysicalCoordinates();
        var screen = Screen(settings); var work = screen.WorkingArea; var scale = Scale(screen);
        var source = PresentationSource.FromVisual(window); var wpfScale = source?.CompositionTarget?.TransformToDevice.M11 ?? scale;
        var correction = scale / wpfScale;
        if (Math.Abs(correction - 1) > .01) content.LayoutTransform = new ScaleTransform(correction, correction); else content.LayoutTransform = Transform.Identity;
        window.MaxHeight = Math.Max(120, Math.Min(584 * scale / wpfScale, (work.Height - 24 * scale) / wpfScale));
        content.MaxHeight = Math.Max(100, Math.Min(560, work.Height / scale - 48));
        content.Width = settings.PanelWidthDip;
        window.UpdateLayout();
        var width = (int)Math.Ceiling((settings.PanelWidthDip + 24) * scale);
        var height = (int)Math.Ceiling(Math.Min(window.ActualHeight * wpfScale, work.Height - 24 * scale));
        var x = work.Right - (int)Math.Round((settings.RightMarginDip + settings.PanelWidthDip + 12) * scale);
        var y = SurfaceTop(work.Top, settings.TopMarginDip, scale);
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        var point = new Native.Point(x, y);
        if (parent != IntPtr.Zero) Native.MapWindowPoints(IntPtr.Zero, parent, ref point, 1);
        Native.SetWindowPos(new WindowInteropHelper(window).Handle, parent == IntPtr.Zero ? new IntPtr(settings.WindowMode == "topmost" ? -1 : -2) : IntPtr.Zero, point.X, point.Y, width, height, Native.SWP_NOACTIVATE);
    }
    public static void Input(Window window, AppSettings settings)
    {
        using var physical = new Native.PhysicalCoordinates();
        var screen = Screen(settings); var work = screen.WorkingArea; var scale = Scale(screen);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var top = SurfaceTop(work.Top, settings.TopMarginDip, scale);
        window.MaxHeight = Math.Max(240, Math.Min(work.Height / scale - 32, (work.Bottom - top) / scale));
        window.Width = Math.Max(240, Math.Min(window.Width, work.Width / scale - 24));
        var width = window.Width;
        var right = work.Right / scale - settings.RightMarginDip - settings.PanelWidthDip - 16;
        window.Left = Math.Max(work.Left / scale + 12, right - width);
        window.Top = top / scale;
        window.Loaded += (_, _) =>
        {
            using var coordinates = new Native.PhysicalCoordinates();
            var handle = new WindowInteropHelper(window).Handle;
            var pixelWidth = (int)Math.Ceiling(window.Width * scale);
            var pixelHeight = (int)Math.Ceiling(Math.Min(window.ActualHeight * scale, window.MaxHeight * scale));
            var x = Math.Clamp(work.Right - (int)Math.Round((settings.RightMarginDip + settings.PanelWidthDip + 16) * scale) - pixelWidth, work.Left, Math.Max(work.Left, work.Right - pixelWidth));
            var y = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - pixelHeight));
            Native.SetWindowPos(handle, IntPtr.Zero, x, y, pixelWidth, pixelHeight, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
        };
    }
}
