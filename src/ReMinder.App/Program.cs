using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using ReMinder.App.Platform;
using ReMinder.App.Services;
using ReMinder.App.UI;

namespace ReMinder.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string Option(string key, string fallback = "") { var index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback; }
        var directory = Path.GetFullPath(Option("--data-dir", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReMinder")));
        Log.DirectoryPath = directory;
        if (args.Contains("--unregister-notifications"))
        {
            try { NotificationService.UninstallRegistration(); return 0; }
            catch (Exception e) { Log.Write("unregister-notifications", e); return 1; }
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Theme.Apply(new AppSettings()); app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/ReMinder;component/UI/Styles.xaml", UriKind.Relative) });
        app.DispatcherUnhandledException += (_, e) => { Log.Write("unhandled", e.Exception); e.Handled = true; MessageBox.Show("操作未能完成，任务数据已保留。\n" + e.Exception.Message, "Re:minder", MessageBoxButton.OK, MessageBoxImage.Warning); };
        if (args.Contains("--panel"))
        {
            if (!int.TryParse(Option("--core-pid"), out var corePid) || string.IsNullOrEmpty(Option("--token"))) return 2;
            using var runtime = new PanelRuntime(Option("--pipe"), Option("--token"), corePid, args.Contains("--qa"));
            app.Startup += async (_, _) => await runtime.StartAsync(); return app.Run();
        }
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "|" + directory.ToUpperInvariant())))[..24];
        using var mutex = new Mutex(true, "Local\\ReMinder.Core." + key, out var primary);
        var activationName = "ReMinder.Activation." + key;
        if (!primary)
        {
            // The notification COM callback is delivered to the existing registration. Ordinary launches request the panel.
            try { using var client = new NamedPipeClientStream(".", activationName, PipeDirection.Out, PipeOptions.Asynchronous); client.Connect(5000); Wire.WriteAsync(client, new WireMessage { Kind = "activate", Action = Option("--action", args.Contains("--silent") || args.Any(a => a.Contains("Toast", StringComparison.OrdinalIgnoreCase)) ? "" : "show") }, new SemaphoreSlim(1, 1)).GetAwaiter().GetResult(); }
            catch (IOException e) { Log.Write("activation-redirect", e); }
            return 0;
        }
        using var coordinator = new CoreCoordinator(directory, args.Contains("--qa")); using var cancellation = new CancellationTokenSource();
        app.Startup += async (_, _) =>
        {
            try { await coordinator.StartAsync(); Log.Write("core-ready"); _ = ActivationLoop(activationName, coordinator, cancellation.Token); if (Option("--action") is { Length: > 0 } action) await coordinator.Execute(new WireMessage { Action = action }); }
            catch (Exception e) { Log.Write("startup", e); MessageBox.Show("Re:minder 无法启动：\n" + e.Message, "Re:minder", MessageBoxButton.OK, MessageBoxImage.Error); app.Shutdown(1); }
        };
        var exitCode = app.Run(); cancellation.Cancel(); mutex.ReleaseMutex(); return exitCode;
    }
    private static async Task ActivationLoop(string name, CoreCoordinator coordinator, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(3000); var message = await Wire.ReadAsync(server, timeout.Token);
                if (message.Kind == "activate" && message.Action is "show" or "new" or "settings" or "reminders" or "history" or "exit") await Application.Current.Dispatcher.InvokeAsync(() => Controls.Safely(() => coordinator.Execute(message)));
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or InvalidDataException) { if (!token.IsCancellationRequested) Log.Write("activation-pipe", e); }
        }
    }
}
