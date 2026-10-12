using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ReMinder.App.Platform;

internal sealed class DesktopHostService
{
    private IntPtr host;
    public string EffectiveMode { get; private set; } = "normal";
    public string Notice { get; private set; } = "";
    public bool Alive => host == IntPtr.Zero || Native.IsWindow(host);
    public IntPtr Host => host;

    public void Apply(IntPtr panel, string requested)
    {
        if (requested == "topmost")
        {
            EffectiveMode = "topmost"; Notice = "";
            Native.SetWindowPos(panel, new IntPtr(-1), 0, 0, 0, 0, Native.SWP_NOACTIVATE | 0x1 | 0x2);
            return;
        }
        if (requested == "normal") { EffectiveMode = "normal"; return; }
        var candidate = FindInteractiveDesktop();
        if (candidate == IntPtr.Zero) { Fallback(panel); return; }
        // Attach above the desktop list-view, rather than behind its full-window input surface.
        var oldStyle = Native.GetWindowLong(panel, Native.GWL_STYLE).ToInt64();
        Native.SetWindowLong(panel, Native.GWL_STYLE, new IntPtr((oldStyle & ~Native.WS_POPUP & ~Native.WS_CAPTION & ~Native.WS_THICKFRAME) | Native.WS_CHILD));
        Marshal.SetLastPInvokeError(0);
        var parent = Native.SetParent(panel, candidate);
        var error = Marshal.GetLastWin32Error();
        if (parent == IntPtr.Zero && error != 0)
        {
            Native.SetWindowLong(panel, Native.GWL_STYLE, new IntPtr(oldStyle)); Fallback(panel); return;
        }
        host = candidate;
        EffectiveMode = "desktop";
        Notice = "";
        Native.SetWindowPos(panel, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED | 0x1 | 0x2);
    }

    private void Fallback(IntPtr panel)
    {
        host = IntPtr.Zero; EffectiveMode = "fallback";
        Notice = "桌面固定暂不可用，已恢复为普通窗口。可在菜单中重试。";
        Native.SetWindowPos(panel, new IntPtr(-2), 0, 0, 0, 0, Native.SWP_NOACTIVATE | 0x1 | 0x2);
    }

    private static IntPtr FindInteractiveDesktop()
    {
        var shellPid = Native.GetWindowThreadProcessId(Native.FindWindow("Shell_TrayWnd", null), out var pid) != 0 ? pid : 0;
        if (shellPid == 0) return IntPtr.Zero;
        IntPtr result = IntPtr.Zero;
        Native.EnumWindows((top, _) =>
        {
            var name = Native.ClassName(top);
            if (name is not "Progman" and not "WorkerW") return true;
            Native.GetWindowThreadProcessId(top, out var owner);
            if (owner != shellPid) return true;
            var view = Native.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero) Native.EnumChildWindows(top, (child, _) => { if (Native.ClassName(child) == "SHELLDLL_DefView") { view = child; return false; } return true; }, IntPtr.Zero);
            if (view == IntPtr.Zero || !Native.IsWindowVisible(view) || !Native.GetClientRect(view, out var rect) || rect.Width < 320 || rect.Height < 100) return true;
            result = view;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    public static object Diagnostics(IntPtr panel)
    {
        var windows = new List<object>();
        Native.EnumWindows((hwnd, _) =>
        {
            var name = Native.ClassName(hwnd);
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd")
            {
                Native.GetWindowRect(hwnd, out var rect); Native.GetWindowThreadProcessId(hwnd, out var pid);
                windows.Add(new { className = name, hwnd = hwnd.ToInt64(), pid, visible = Native.IsWindowVisible(hwnd), rect.Left, rect.Top, rect.Width, rect.Height, dpi = Native.GetDpiForWindow(hwnd), dpiContext = Native.GetWindowDpiAwarenessContext(hwnd).ToInt64() });
            }
            return true;
        }, IntPtr.Zero);
        Native.GetWindowRect(panel, out var panelRect);
        return new { os = Environment.OSVersion.Version.ToString(), windows, panel = new { pid = Environment.ProcessId, hwnd = panel.ToInt64(), parent = Native.GetParent(panel).ToInt64(), visible = Native.IsWindowVisible(panel), style = Native.GetWindowLong(panel, Native.GWL_STYLE).ToInt64(), extendedStyle = Native.GetWindowLong(panel, Native.GWL_EXSTYLE).ToInt64(), panelRect.Left, panelRect.Top, panelRect.Width, panelRect.Height, dpi = Native.GetDpiForWindow(panel), dpiContext = Native.GetWindowDpiAwarenessContext(panel).ToInt64() } };
    }
}
