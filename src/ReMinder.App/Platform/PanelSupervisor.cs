using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Windows.Threading;
using ReMinder.App.Services;

namespace ReMinder.App.Platform;

internal sealed class PanelSupervisor(StateStore store, string dataDirectory, bool qa) : IDisposable
{
    private NamedPipeServerStream? pipe; private Process? process; private CancellationTokenSource? cancellation;
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly Dispatcher dispatcher = Application.Current.Dispatcher;
    private bool connected, disposed; private int generation, failures;
    private readonly System.Collections.Concurrent.ConcurrentQueue<WireMessage> pendingToasts = new();
    public string Status { get; private set; } = "桌面面板正在启动。";
    public bool Running => process is not null && !process.HasExited;
    public event Action<WireMessage>? Command;
    public event Action? StatusChanged;
    public void Start()
    {
        if (disposed) return; StopChild(); var current = ++generation; connected = false;
        var name = "ReMinder.Panel." + Guid.NewGuid().ToString("N"); var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cancellation = new CancellationTokenSource(); pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--panel", "--pipe", name, "--token", token, "--core-pid", Environment.ProcessId.ToString(), "--data-dir", dataDirectory }) start.ArgumentList.Add(arg);
        if (qa) start.ArgumentList.Add("--qa");
        process = Process.Start(start) ?? throw new IOException("无法启动桌面面板。"); process.EnableRaisingEvents = true;
        process.Exited += (_, _) => dispatcher.BeginInvoke(() => Recover(current));
        _ = ReadLoop(pipe, process, token, current, cancellation.Token);
    }
    private async Task ReadLoop(NamedPipeServerStream stream, Process child, string token, int current, CancellationToken cancellationToken)
    {
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); handshake.CancelAfter(TimeSpan.FromSeconds(15));
            await stream.WaitForConnectionAsync(handshake.Token);
            if (!Native.GetNamedPipeClientProcessId(stream.SafePipeHandle.DangerousGetHandle(), out var pid) || pid != child.Id) throw new InvalidDataException("面板进程身份不匹配。");
            var hello = await Wire.ReadAsync(stream, handshake.Token); if (hello.Kind != "hello" || hello.Token != token) throw new InvalidDataException("面板连接校验失败。");
            connected = true; await SendAsync(new WireMessage { Kind = "snapshot", State = store.Snapshot });
            while (pendingToasts.TryDequeue(out var pending)) await SendAsync(pending);
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await Wire.ReadAsync(stream, cancellationToken);
                Log.Write("panel-message-" + message.Kind);
                await dispatcher.InvokeAsync(() =>
                {
                    if (current != generation) return;
                    if (message.Kind == "host") { Status = message.Text; StatusChanged?.Invoke(); try { File.WriteAllText(Path.Combine(dataDirectory, "desktop-diagnostics.json"), message.Mode); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("desktop-diagnostics-save", e); } }
                    else if (message.Kind == "command") Command?.Invoke(message);
                });
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or InvalidDataException or ObjectDisposedException)
        { if (!cancellationToken.IsCancellationRequested) { Log.Write("panel-ipc", e); await dispatcher.InvokeAsync(() => Recover(current)); } }
    }
    private void Recover(int current)
    {
        if (disposed || current != generation) return;
        Status = "面板意外关闭，正在重建。"; StatusChanged?.Invoke();
        if (++failures <= 3) Start();
        else { StopChild(); Status = "面板重建未成功，请从托盘重试。"; StatusChanged?.Invoke(); Log.Write("panel-restart-limit"); }
    }
    public async Task SendAsync(WireMessage message)
    {
        var stream = pipe; if (!connected || stream is null) { if (message.Kind == "toast") { pendingToasts.Enqueue(message); while (pendingToasts.Count > 5) pendingToasts.TryDequeue(out _); } return; }
        try { await Wire.WriteAsync(stream, message, writer, cancellation?.Token ?? default); }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException) { Log.Write("panel-send", e); }
    }
    public void Refresh() => _ = SendAsync(new WireMessage { Kind = "snapshot", State = store.Snapshot });
    public void Retry() { failures = 0; Start(); }
    private void StopChild()
    {
        ++generation; connected = false; cancellation?.Cancel(); pipe?.Dispose(); pipe = null;
        if (process is not null) { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } process.Dispose(); process = null; }
        cancellation?.Dispose(); cancellation = null;
    }
    public void Dispose() { disposed = true; StopChild(); }
}
