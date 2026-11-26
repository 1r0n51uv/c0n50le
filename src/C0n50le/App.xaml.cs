using C0n50le.Services;
using Microsoft.UI.Xaml;

namespace C0n50le;

/// <summary>
/// One c0n50le at a time: a second start (the shortcut, the Run entry) just brings the running one forward.
/// "--tray" starts hidden in the notification area; "--startup" (the Run entry) follows System → Start.
/// </summary>
public partial class App : Application
{
    private const string InstanceName = @"Local\c0n50le";
    private const string ShowEventName = @"Local\c0n50le.Show";
    private Mutex? _instance;
    private EventWaitHandle? _show;
    private RegisteredWaitHandle? _wait;
    private MainWindow? _window;

    public App()
    {
        UnhandledException += (_, args) => AppLog.Write("Errore: " + args.Exception);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _instance = new Mutex(false, InstanceName, out var first);
        if (!first)
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                using (existing) existing.Set();
            Exit();
            return;
        }
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var start = arguments.Contains(StartupRegistration.TrayArgument, StringComparer.OrdinalIgnoreCase) ? StartMode.Tray
            : arguments.Contains("--startup", StringComparer.OrdinalIgnoreCase) ? StartMode.Startup : StartMode.Normal;
        var window = _window = new MainWindow(start);
        window.Closed += (_, _) =>
        {
            _wait?.Unregister(null);
            _show?.Dispose();
            _instance?.Dispose();
        };
        _wait = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) =>
            window.DispatcherQueue.TryEnqueue(() => window.ShowHome()), null, Timeout.Infinite, false);
        window.Start();
    }
}

public enum StartMode
{
    /// <summary>Opened by the user: the home shows.</summary>
    Normal,
    /// <summary>Opened at sign-in: full screen or tray, as chosen in System → Start.</summary>
    Startup,
    /// <summary>Hidden in the notification area.</summary>
    Tray
}
