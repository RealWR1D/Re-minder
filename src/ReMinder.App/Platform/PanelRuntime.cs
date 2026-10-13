using System.IO.Pipes;
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Text.Json;
using ReMinder.App.UI;
using ReMinder.App.Services;

namespace ReMinder.App.Platform;

internal sealed class PanelRuntime : IDisposable
{
    private readonly NamedPipeClientStream pipe; private readonly SemaphoreSlim writer = new(1, 1); private readonly CancellationTokenSource cancellation = new();
    private readonly Window window; private readonly PanelView view; private readonly PanelViewModel model;
    private readonly DesktopHostService host = new(); private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer health = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly string token; private readonly int corePid; private readonly bool qa;
    private bool attached, positioned; private AppSettings settings = new();
    public PanelRuntime(string name, string token, int corePid, bool qa)
    {
        this.token = token; this.corePid = corePid; this.qa = qa;
        pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        model = new PanelViewModel(message => { Native.AllowSetForegroundWindow((uint)corePid); _ = Send(message); }); view = new PanelView { DataContext = model };
        var border = new Border { Child = view, Margin = new Thickness(FrameWindow.SurfaceInsetDip), Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = .12 } };
        window = new Window { Title = "Re:minder", Content = border, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = qa, ShowActivated = false };
        window.Loaded += (_, _) => Attach(); view.SizeChanged += (_, _) => { if (attached && !positioned) { positioned = true; window.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { Position(); positioned = false; }); } };
        clock.Tick += (_, _) => model.RefreshClock();
        health.Tick += (_, _) =>
        {
            try { using var process = Process.GetProcessById(corePid); if (process.HasExited) Application.Current.Shutdown(); }
            catch (ArgumentException) { Application.Current.Shutdown(); }
            if (attached && settings.WindowMode == "desktop" && !qa && !host.Alive) Application.Current.Shutdown();
        };
        window.Closed += (_, _) => Application.Current.Shutdown();
    }
    public async Task StartAsync()
    {
        try
        {
            await pipe.ConnectAsync(10000, cancellation.Token);
            if (!Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) || pid != corePid) throw new InvalidDataException("后台进程身份不匹配。");
            await Send(new WireMessage { Kind = "hello", Token = token });
            _ = ReadLoop(); clock.Start(); health.Start();
        }
        catch (Exception e) { Log.Write("panel-start", e); Application.Current.Shutdown(1); }
    }
    private async Task ReadLoop()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var message = await Wire.ReadAsync(pipe, cancellation.Token);
                await window.Dispatcher.InvokeAsync(() =>
                {
                    switch (message.Kind)
                    {
                        case "snapshot" when message.State is not null:
                            if (message.State.StateVersion < model.State.StateVersion) break;
                            settings = message.State.Settings; Theme.Apply(settings); view.ApplySnapshot(message.State);
                            if (settings.PanelVisible) { if (!window.IsVisible) { window.Show(); Native.ShowWindow(new WindowInteropHelper(window).Handle, 4); } Position(); } else window.Hide();
                            break;
                        case "toast": view.ShowToast(message.Text, message.CanUndo); break;
                        case "focus": if (!window.IsVisible) window.Show(); view.FocusTask(message.TaskId); break;
                    }
                });
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException) { Log.Write("panel-disconnected", e); await window.Dispatcher.InvokeAsync(() => Application.Current.Shutdown()); }
    }
    private void Attach()
    {
        if (attached) return; attached = true;
        var handle = new WindowInteropHelper(window).Handle; host.Apply(handle, qa ? "normal" : settings.WindowMode); model.HostNotice = host.Notice; model.Apply(model.State);
        HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled) => { if (message is 0x2e0 or 0x7e or 0x1a) window.Dispatcher.BeginInvoke(Position); return IntPtr.Zero; });
        Position();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => _ = Send(new WireMessage { Kind = "host", Text = host.EffectiveMode switch { "desktop" => "面板已连接到 Windows 桌面层。", "topmost" => "面板当前始终置顶。", "normal" => "验收模式：普通窗口。", _ => host.Notice }, Mode = JsonSerializer.Serialize(DesktopHostService.Diagnostics(handle), Serialization.Json) }));
    }
    private void Position() { if (attached && window.IsVisible) PlacementService.Panel(window, view, settings, host.Host); }
    private Task Send(WireMessage message) => Wire.WriteAsync(pipe, message, writer, cancellation.Token);
    public void Dispose() { clock.Stop(); health.Stop(); cancellation.Cancel(); pipe.Dispose(); cancellation.Dispose(); }
}
