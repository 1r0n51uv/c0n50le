using C0n50le.Core.Catalog;
using C0n50le.Core.Input;
using C0n50le.Core.Library;
using C0n50le.Core.Localization;
using C0n50le.Core.Settings;
using C0n50le.Core.Theming;
using C0n50le.Services;
using C0n50le.Ui;
using C0n50le.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace C0n50le;

/// <summary>
/// The one window of c0n50le: the home, System over it, the pickers, the tour and the intro. Split by topic into the
/// MainWindow.*.cs files.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly StartMode _startMode;
    private readonly AppPaths _paths;
    private readonly Preferences _prefs;
    private readonly GameCatalog _catalog;
    private readonly nint _handle;
    private readonly DispatcherQueueTimer _clockTimer;
    private readonly DispatcherQueueTimer _updateTimer;
    private readonly string? _loadProblem;
    private TrayIcon? _tray;
    private bool _exiting, _shownOnce, _started;

    public HomeViewModel Home { get; } = new();

    public MainWindow(StartMode startMode)
    {
        _startMode = startMode;
        _paths = AppPaths.ForThisApp();
        AppLog.UseFile(_paths.LogFile);
        _translation = new TranslationService(_paths.Data);
        _translation.Changed += () => DispatcherQueue.TryEnqueue(UpdateTranslatorRow);
        CursorHider.Initialize(_paths.Data);
        try { _prefs = Preferences.Load(_paths.PreferencesFile); }
        catch (Exception ex)
        {
            _prefs = new Preferences();
            _loadProblem = ex.Message;
            AppLog.Write($"Preferenze non leggibili: {ex.Message}");
        }
        if (_prefs.Language.Length == 0 && StartupRegistration.InstallerLanguage() is { } installer && Strings.Languages.Contains(installer))
            _prefs.Language = installer;
        Strings.Use(Strings.Resolve(_prefs.Language));
        _catalog = new GameCatalog(Updater.CurrentVersion);

        InitializeComponent();
        Title = "c0n50le";
        _handle = WindowNative.GetWindowHandle(this);
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "c0n50le.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        AppWindow.Closing += OnClosing;
        Activated += OnActivated;

        GlassSurface.LiquidFailed += error => DispatcherQueue.TryEnqueue(() =>
        {
            AppLog.Write($"Vetro liquido non disponibile: {error}");
            ShowToast(Strings.Get("LiquidGlassUnavailable"));
        });
        ThemeManager.Use(_prefs.Theme, (Core.Theming.GlassDepth)_prefs.Glass);
        Root.RequestedTheme = ThemeManager.ElementTheme;
        Sounds.Enabled = _prefs.Sounds;

        _clockTimer = DispatcherQueue.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(5);
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();

        _updateTimer = DispatcherQueue.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromHours(6);
        _updateTimer.Tick += (_, _) => _ = CheckUpdatesAsync(interactive: false);

        Root.SizeChanged += (_, _) => UpdateStage();
        InitializeInput();
        InitializeLibrary();
        InitializeWidgets();
        ApplyTexts();
        Strings.Changed += ApplyTexts;
        AppLog.Write($"Avvio c0n50le {Updater.CurrentVersion} ({_startMode})");
    }

    /// <summary>Opens the home, or stays in the tray, depending on how c0n50le was started.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _tray = new TrayIcon(() => DispatcherQueue.TryEnqueue(() => ShowHome()), () => DispatcherQueue.TryEnqueue(ExitApp));
        ReturnHotkey.Hook(_handle, () => DispatcherQueue.TryEnqueue(() => ShowHome()));
        RegisterKeyboardShortcut(report: false);
        _clockTimer.Start();
        _updateTimer.Start();
        StartPolling();
        var show = _startMode switch
        {
            StartMode.Tray => false,
            StartMode.Startup => _prefs.OpenFullscreen,
            _ => true
        };
        _ = LoadLibraryAsync(firstLoad: true);
        if (show) ShowHome(first: true);
        if (_loadProblem is not null) ShowToast(Strings.Format("PreferencesUnreadable", _loadProblem));
        if (_prefs.CheckUpdates) _ = CheckUpdatesAsync(interactive: false);
    }

    /// <summary>Brings the home forward, full screen on the chosen screen.</summary>
    public void ShowHome(bool first = false)
    {
        if (_exiting) return;
        var firstShow = !_shownOnce;
        _shownOnce = true;
        AppWindow.Show();
        Activate();
        try { PlaceWindow(); }
        catch (Exception ex) { AppLog.Write($"Schermo intero: {ex.Message}"); }
        UpdateInputLabel();
        NativeWindows.BringForward(_handle);
        if (firstShow) BeginIntro();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_session is not null) RefreshHero();
            if (!IsOverlayOpen && !PageOpen && !IntroVisible) FocusSelection();
        });
    }

    private void PlaceWindow()
    {
        var area = Displays.Find(_prefs.Screen);
        if (area is null) return;
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen
            && DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary)?.DisplayId.Value == area.DisplayId.Value)
            return;
        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        var bounds = area.WorkArea;
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, Math.Max(1, bounds.Width / 2), Math.Max(1, bounds.Height / 2)));
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }

    /// <summary>Hides the home (back to the desktop or the game); it stays in the tray.</summary>
    private void HideHome()
    {
        CloseOverlays();
        RestoreCursor();
        HomeMusic.Set(false, DispatcherQueue);
        AppWindow.Hide();
    }

    /// <summary>Lays the interface out at 1080 high and scales it to the window, times the chosen scale.</summary>
    private void UpdateStage()
    {
        var width = Root.ActualWidth;
        var height = Root.ActualHeight;
        if (width <= 0 || height <= 0) return;
        var (layoutWidth, layoutHeight) = UiScale.LayoutSize(width, height, _prefs.UiScale);
        Stage.Width = layoutWidth;
        Stage.Height = layoutHeight;
        ScaleBox.Width = width;
        ScaleBox.Height = height;
    }

    private void UpdateClock() => Home.Clock = DateTime.Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture);

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        var active = args.WindowActivationState != WindowActivationState.Deactivated;
        UpdateMusic(active);
        // With a game running, the home in front silences it and plays its own music; back to the game, the reverse.
        if (_session is { } session) GameAudio.Set(session.ProcessIds(), mute: active && AppWindow.IsVisible);
        else if (!active) GameAudio.RestoreAll();
        if (!active) RestoreCursor();
        else
        {
            if (_input.Mode == InputMode.Controller) HideCursor();
            DispatcherQueue.TryEnqueue(TryPromptPendingUpdate);
        }
    }

    private void UpdateMusic(bool active) =>
        HomeMusic.Set(active && _prefs.Music && AppWindow.IsVisible && !IntroVisible, DispatcherQueue);

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting || !_prefs.CloseToTray) { Shutdown(); return; }
        args.Cancel = true;
        HideHome();
    }

    private void ExitApp()
    {
        _exiting = true;
        Shutdown();
        Close();
    }

    private void Shutdown()
    {
        _exiting = true;
        Strings.Changed -= ApplyTexts;
        // A recording still running is finished, so the file stays playable.
        if (_recorder is { } recorder)
        {
            _recorder = null;
            try { Task.Run(recorder.StopAsync).Wait(TimeSpan.FromSeconds(8)); }
            catch (Exception ex) { AppLog.Write($"Registrazione all'uscita: {ex.Message}"); }
        }
        _recordingIndicator?.Close();
        _recordingIndicator = null;
        foreach (var watcher in _galleryWatchers) watcher.Dispose();
        GameAudio.RestoreAll();
        _translation.Dispose();
        _pollTimer?.Stop();
        _sessionTimer?.Stop();
        _backdropTimer?.Stop();
        _clockTimer.Stop();
        _updateTimer.Stop();
        _feedTimer?.Stop();
        _galleryTimer?.Stop();
        _widgetRequest?.Cancel();
        _artworkLookup?.Cancel();
        _descriptionLifetime.Cancel();
        _session?.Dispose();
        RestoreCursor();
        ReturnHotkey.Unhook(_handle);
        _tray?.Dispose();
        _tray = null;
        HomeMusic.Set(false, DispatcherQueue);
        Save();
        AppLog.Write("Uscita");
    }

    private void Save()
    {
        if (_loadProblem is not null) return;
        try { _prefs.Save(_paths.PreferencesFile); }
        catch (Exception ex) { ShowToast(Strings.Format("SaveFailed", ex.Message)); }
    }

    /// <summary>Every text that isn't bound: after the language changes.</summary>
    private void ApplyTexts()
    {
        Home.SystemLabel = Strings.Get("System");
        SystemTitle.Text = Strings.Get("System");
        IntroTagline.Text = Strings.Get("IntroTagline").ToUpperInvariant();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SystemPill, Strings.Get("System"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(DesktopPill, Strings.Get("BackToDesktop"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SystemBack, Strings.Get("HintBack"));
        UpdateInputLabel();
        Home.RefreshTexts();
        RefreshHero();
        RenderCards();
        ApplyWidgets(reload: true);
        if (SystemPage.Visibility == Visibility.Visible) RenderSystem();
    }

    private void OnSystemClick(object sender, RoutedEventArgs e) => OpenSystem();

    private void OnDesktopClick(object sender, RoutedEventArgs e)
    {
        Sounds.Play(Core.Audio.UiSound.Back);
        HideHome();
    }
}
