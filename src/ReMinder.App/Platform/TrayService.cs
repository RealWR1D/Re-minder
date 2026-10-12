using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Drawing = System.Drawing;

namespace ReMinder.App.Platform;

internal sealed class TrayService : IDisposable
{
    private const uint Callback = 0x8000 + 73;
    private readonly HwndSource source;
    private readonly Drawing.Icon icon;
    private Native.NotifyIconData data;
    private readonly DispatcherTimer retry = new() { Interval = TimeSpan.FromSeconds(2) };
    private int attempts;
    private readonly uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    public event Action<string>? Command;
    public event Action? ShellRestarted;
    public event Action? EnvironmentChanged;
    public bool Visible { get; private set; }
    public bool PanelVisible { get; set; } = true;
    public bool Topmost { get; set; }
    public IntPtr Handle => source.Handle;
    public TrayService()
    {
        source = new HwndSource(new HwndSourceParameters("Re:minder 系统接收器") { Width = 0, Height = 0, WindowStyle = unchecked((int)Native.WS_POPUP), ExtendedWindowStyle = (int)Native.WS_EX_TOOLWINDOW });
        source.AddHook(WndProc);
        Native.WTSRegisterSessionNotification(source.Handle, 0);
        icon = new Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "re-minder.ico"), 32, 32);
        data = new Native.NotifyIconData { cbSize = (uint)Marshal.SizeOf<Native.NotifyIconData>(), hWnd = source.Handle, uID = 1, uFlags = 1 | 2 | 4 | 0x20, uCallbackMessage = Callback, hIcon = icon.Handle, szTip = "Re:minder · 截止日期面板", szInfo = "", szInfoTitle = "", guidItem = new Guid("76a2a1ec-ea31-43fc-82e8-d158846187b7") };
        retry.Tick += (_, _) => { if (Add() || ++attempts >= 5) retry.Stop(); };
        if (!Add()) retry.Start();
    }
    private bool Add()
    {
        Visible = Native.Shell_NotifyIcon(0, ref data);
        if (Visible) { data.uVersion = 4; Native.Shell_NotifyIcon(4, ref data); }
        return Visible;
    }
    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == taskbarCreated) { Visible = false; attempts = 0; if (!Add()) retry.Start(); ShellRestarted?.Invoke(); }
        else if (message is 0x218 or 0x1e or 0x7e or 0x1a or 0x2b1) EnvironmentChanged?.Invoke();
        else if ((uint)message == Callback)
        {
            var code = (int)(lParam.ToInt64() & 0xffff);
            if (code is 0x400 or 0x401 or 0x203) Command?.Invoke("show");
            else if (code == 0x7b || code == 0x205) OpenMenu();
            handled = true;
        }
        return IntPtr.Zero;
    }
    private void OpenMenu()
    {
        var menu = Native.CreatePopupMenu();
        try
        {
            var items = new (string Label, string Action)[] { (PanelVisible ? "隐藏面板" : "显示面板", "visibility"), ("新建任务", "new"), ("待处理提醒", "reminders"), ("始终置顶", "topmost"), ("重试桌面固定", "retry-desktop"), ("设置", "settings"), ("退出 Re:minder", "exit") };
            for (var i = 0; i < items.Length; i++) Native.AppendMenu(menu, i == 3 && Topmost ? 8u : 0u, (nuint)(100 + i), items[i].Label);
            Native.GetCursorPos(out var point); Native.SetForegroundWindow(source.Handle);
            var selected = Native.TrackPopupMenuEx(menu, 0x100 | 0x2, point.X, point.Y, source.Handle, IntPtr.Zero);
            Native.PostMessage(source.Handle, 0, IntPtr.Zero, IntPtr.Zero);
            if (selected >= 100 && selected < 100 + items.Length) Command?.Invoke(items[selected - 100].Action);
        }
        finally { Native.DestroyMenu(menu); }
    }
    public void Dispose() { retry.Stop(); Native.WTSUnRegisterSessionNotification(source.Handle); Native.Shell_NotifyIcon(2, ref data); source.Dispose(); icon.Dispose(); }
}
