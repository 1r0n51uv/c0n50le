using System.Globalization;
using C0n50le.Core.Audio;
using C0n50le.Core.Localization;
using C0n50le.Core.Widgets;
using C0n50le.Services;
using C0n50le.Ui;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace C0n50le;

/// <summary>The home widgets on the right of the top shelf: weather, news and a gallery, each from a source set in Settings.</summary>
public sealed partial class MainWindow
{
    private static readonly HttpClient WidgetHttp = CreateWidgetClient();
    private static readonly TimeSpan FeedRefresh = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan GalleryInterval = TimeSpan.FromSeconds(12);

    private DispatcherQueueTimer? _feedTimer, _galleryTimer;
    private CancellationTokenSource? _widgetRequest;
    private Gallery _gallery = new([]);
    private string? _galleryLoaded;
    private int _galleryRequest;

    private static HttpClient CreateWidgetClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("c0n50le/" + Updater.CurrentVersion);
        return http;
    }

    private string GalleryFolder =>
        _prefs.GalleryFolder.Length > 0 ? _prefs.GalleryFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    private void InitializeWidgets()
    {
        _feedTimer = DispatcherQueue.CreateTimer();
        _feedTimer.Interval = FeedRefresh;
        _feedTimer.Tick += (_, _) => _ = RefreshFeedsAsync(skeleton: false);
        _galleryTimer = DispatcherQueue.CreateTimer();
        _galleryTimer.Interval = GalleryInterval;
        // The folder is watched: a new picture (a screenshot just taken) is read at the next tick.
        _galleryTimer.Tick += (_, _) =>
        {
            if (_galleryDirty) { _galleryDirty = false; _ = LoadGalleryAsync(); }
            else ShowNextPicture();
        };
        ElementCompositionPreview.GetElementVisual(GalleryGrid).Opacity = 0;
        // ApplyTexts, right after, shows them and fetches the first time.
    }

    /// <summary>Shows or hides the widgets and, after a change of source, fetches them again.</summary>
    private void ApplyWidgets(bool reload)
    {
        if (_feedTimer is null || _galleryTimer is null) return;
        var show = _prefs.ShowWidgets && (_prefs.ShowWeather || _prefs.ShowNews || _prefs.ShowGallery);
        WidgetColumn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        WeatherWidget.Visibility = _prefs.ShowWeather ? Visibility.Visible : Visibility.Collapsed;
        NewsWidget.Visibility = _prefs.ShowNews ? Visibility.Visible : Visibility.Collapsed;
        GalleryWidget.Visibility = _prefs.ShowGallery ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            _feedTimer.Stop();
            _galleryTimer.Stop();
            _widgetRequest?.Cancel();
            return;
        }
        if (reload || !_feedTimer.IsRunning) _ = RefreshFeedsAsync(skeleton: reload);
        _feedTimer.Start();
        if (_prefs.ShowGallery && (reload || _galleryLoaded != GalleryFolder)) _ = LoadGalleryAsync();
        _galleryTimer.Start();
    }

    private async Task RefreshFeedsAsync(bool skeleton)
    {
        _widgetRequest?.Cancel();
        var request = _widgetRequest = new CancellationTokenSource();
        // While a source loads the widget keeps its shape, as a softly pulsing skeleton, instead of an empty panel.
        if (_prefs.ShowWeather && _prefs.WeatherCity.Length > 0 && (skeleton || WeatherWidget.Content is null))
            WeatherWidget.Content = WeatherSkeleton();
        if (_prefs.ShowNews && (skeleton || NewsWidget.Content is null)) NewsWidget.Content = NewsSkeleton();
        var work = new List<Task>();
        if (_prefs.ShowWeather) work.Add(RefreshWeatherAsync(request.Token));
        if (_prefs.ShowNews) work.Add(RefreshNewsAsync(request.Token));
        await Task.WhenAll(work);
    }

    // ───────── Skeletons ─────────

    private static Border Bone(double width, double height, double radius = 8) => new()
    {
        Width = width, Height = height, CornerRadius = new CornerRadius(radius), HorizontalAlignment = HorizontalAlignment.Left,
        Background = ThemeManager.Brush("GlyphCircleBrush")
    };

    private static FrameworkElement Pulse(FrameworkElement element)
    {
        element.Loaded += (_, _) => StartPulse(element);
        element.Unloaded += (_, _) => StopPulse(element);
        return element;
    }

    private static void StartPulse(UIElement element)
    {
        if (!Motion.Enabled(Motion.Reduce)) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var pulse = visual.Compositor.CreateScalarKeyFrameAnimation();
        pulse.InsertKeyFrame(0, 1f);
        pulse.InsertKeyFrame(0.5f, 0.45f);
        pulse.InsertKeyFrame(1, 1f);
        pulse.Duration = TimeSpan.FromMilliseconds(1400);
        pulse.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", pulse);
    }

    private static void StopPulse(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
    }

    private void HideGallerySkeleton()
    {
        StopPulse(GallerySkeleton);
        GallerySkeleton.Visibility = Visibility.Collapsed;
    }

    private static FrameworkElement WeatherSkeleton()
    {
        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Border
        {
            Width = 56, Height = 56, CornerRadius = new CornerRadius(28), VerticalAlignment = VerticalAlignment.Center,
            Background = ThemeManager.Brush("GlyphCircleBrush")
        });
        var lines = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Bone(150, 22));
        lines.Children.Add(Bone(110, 16));
        lines.Children.Add(Bone(170, 14));
        Grid.SetColumn(lines, 1);
        grid.Children.Add(lines);
        var now = Bone(84, 52, 12);
        now.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(now, 2);
        grid.Children.Add(now);
        return Pulse(grid);
    }

    private static FrameworkElement NewsSkeleton()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Bone(140, 14, 7));
        foreach (var width in new[] { 380.0, 330.0, 360.0 })
        {
            var item = new StackPanel { Spacing = 7 };
            item.Children.Add(Bone(width, 18));
            item.Children.Add(Bone(width * 0.6, 18));
            panel.Children.Add(item);
        }
        return Pulse(panel);
    }

    // ───────── Weather ─────────

    private async Task RefreshWeatherAsync(CancellationToken token)
    {
        var city = _prefs.WeatherCity;
        if (city.Length == 0)
        {
            WeatherWidget.Content = WidgetMessage("WidgetWeather", Strings.Get("WeatherSetCity"));
            return;
        }
        try
        {
            var report = await new WeatherClient(WidgetHttp).GetAsync(city, _prefs.WeatherFahrenheit, Strings.Language, token);
            if (token.IsCancellationRequested) return;
            WeatherWidget.Content = report is null
                ? WidgetMessage("WidgetWeather", Strings.Format("CityNotFound", city))
                : WeatherView(report);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppLog.Write($"Meteo: {ex.Message}");
            WeatherWidget.Content = WidgetMessage("WidgetWeather", Strings.Get("WeatherUnavailable"));
        }
    }

    private FrameworkElement WeatherView(WeatherReport report)
    {
        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = WeatherEmoji(report.Kind, report.IsDay), FontSize = 52, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Emoji"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var place = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        place.Children.Add(new TextBlock
        {
            Text = report.Place, FontSize = 25, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = ThemeManager.Brush("TextBrush")
        });
        place.Children.Add(new TextBlock
        {
            Text = Strings.Get("Weather_" + report.Kind), FontSize = 21, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = ThemeManager.Brush("TextSecondaryBrush")
        });
        place.Children.Add(new TextBlock
        {
            Text = Strings.Format("WeatherRange", Degrees(report.Low), Degrees(report.High)), FontSize = 19, FontWeight = FontWeights.Medium,
            Foreground = ThemeManager.Brush("TextSecondaryBrush")
        });
        Grid.SetColumn(place, 1);
        grid.Children.Add(place);
        var now = new TextBlock
        {
            Text = Degrees(report.Temperature) + "°", FontSize = 58, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeManager.Brush("TextBrush")
        };
        Grid.SetColumn(now, 2);
        grid.Children.Add(now);
        return grid;
    }

    private static string Degrees(double value) => Math.Round(value).ToString("0", CultureInfo.CurrentCulture);

    private static string WeatherEmoji(WeatherKind kind, bool day) => kind switch
    {
        WeatherKind.Clear => day ? "☀️" : "🌙",
        WeatherKind.PartlyCloudy => day ? "⛅" : "☁️",
        WeatherKind.Cloudy => "☁️",
        WeatherKind.Fog => "🌫️",
        WeatherKind.Drizzle => "🌦️",
        WeatherKind.Rain => "🌧️",
        WeatherKind.Snow => "❄️",
        WeatherKind.Showers => "🌦️",
        WeatherKind.Thunderstorm => "⛈️",
        _ => "☁️"
    };

    // ───────── News ─────────

    private async Task RefreshNewsAsync(CancellationToken token)
    {
        try
        {
            var feed = await News.GetAsync(WidgetHttp, News.Resolve(_prefs.NewsFeed, Strings.Language), 3, token);
            if (token.IsCancellationRequested) return;
            NewsWidget.Content = feed.Items.Count == 0 ? WidgetMessage("WidgetNews", Strings.Get("NewsUnavailable")) : NewsView(feed);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppLog.Write($"Notizie: {ex.Message}");
            NewsWidget.Content = WidgetMessage("WidgetNews", Strings.Get("NewsUnavailable"));
        }
    }

    private static FrameworkElement NewsView(NewsFeed feed)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(WidgetHeader(feed.Source.Length > 0 ? $"{Strings.Get("WidgetNews")} · {feed.Source}" : Strings.Get("WidgetNews")));
        foreach (var item in feed.Items)
            panel.Children.Add(new TextBlock
            {
                Text = item.Title, FontSize = 21, FontWeight = FontWeights.Medium, LineHeight = 27,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis, Foreground = ThemeManager.Brush("TextBrush")
            });
        return panel;
    }

    // ───────── Gallery ─────────

    private readonly List<FileSystemWatcher> _galleryWatchers = [];
    private volatile bool _galleryDirty;
    private IReadOnlyList<string> _galleryShown = [];

    /// <summary>The folders the gallery reads: the chosen pictures folder and the screen recordings.</summary>
    private string[] GalleryFolders => [GalleryFolder, RecordingsFolder];

    private void WatchGallery(string[] folders)
    {
        var wanted = folders.Where(Directory.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.SequenceEqual(_galleryWatchers.Select(watcher => watcher.Path), StringComparer.OrdinalIgnoreCase)) return;
        foreach (var old in _galleryWatchers) old.Dispose();
        _galleryWatchers.Clear();
        foreach (var folder in wanted)
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName
                };
                FileSystemEventHandler changed = (_, args) => { if (Gallery.Extensions.Contains(Path.GetExtension(args.FullPath))) _galleryDirty = true; };
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Changed += changed;
                watcher.Renamed += (_, _) => _galleryDirty = true;
                watcher.EnableRaisingEvents = true;
                _galleryWatchers.Add(watcher);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                AppLog.Write($"Galleria: impossibile osservare {folder}: {ex.Message}");
            }
        }
    }

    private async Task LoadGalleryAsync()
    {
        var folders = GalleryFolders;
        var folder = GalleryFolder;
        _galleryLoaded = folder;
        WatchGallery(folders);
        GalleryMessage.Text = "";
        if (GalleryGrid.Children.Count == 0)
        {
            GallerySkeleton.Visibility = Visibility.Visible;
            StartPulse(GallerySkeleton);
        }
        _gallery = await Task.Run(() => Gallery.Load(folders));
        if (_galleryLoaded != folder) return;
        GalleryMessage.Text = _gallery.Count == 0 ? Strings.Get("GalleryEmpty") : "";
        GalleryOpen.IsEnabled = _gallery.Count > 0;
        AutomationProperties.SetName(GalleryOpen, Strings.Get("ShowGallery"));
        if (_gallery.Count == 0)
        {
            HideGallerySkeleton();
            GalleryGrid.Children.Clear();
            _galleryShown = [];
        }
        ShowNextPicture();
    }

    /// <summary>
    /// Shows the newest screenshots and videos, up to four, laid out by how many there are; the set fades in once they
    /// have opened. It changes only when the folders do.
    /// </summary>
    private void ShowNextPicture()
    {
        if (!_prefs.ShowWidgets || !_prefs.ShowGallery || !AppWindow.IsVisible || HomePage.Visibility != Visibility.Visible || ViewerOpen) return;
        var batch = _gallery.Newest();
        if (batch.Count == 0 || (batch.SequenceEqual(_galleryShown) && GalleryGrid.Children.Count > 0)) return;
        _galleryShown = batch;
        var request = ++_galleryRequest;
        var cells = Gallery.Layout(batch.Count);
        var scale = Math.Max(1, Root.XamlRoot?.RasterizationScale ?? 1);
        var tiles = new List<FrameworkElement>();
        var pending = batch.Count;
        void Opened()
        {
            if (--pending > 0 || request != _galleryRequest) return;
            HideGallerySkeleton();
            Motion.Fade(GalleryGrid, 1, 500, _prefs.ReduceMotion);
        }
        for (var index = 0; index < batch.Count; index++)
        {
            var cell = cells[index];
            var width = (int)Math.Ceiling((cell.ColumnSpan == 2 ? 460 : 230) * scale);
            tiles.Add(GalleryTile(batch[index], cell, batch.Count == 1 ? 30 : 18, width, Opened));
        }
        // The old set fades out first, so the widget never shows half of each.
        Motion.Fade(GalleryGrid, 0, 250, _prefs.ReduceMotion);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            if (!_prefs.ReduceMotion) await Task.Delay(260);
            if (request != _galleryRequest) return;
            GalleryGrid.Children.Clear();
            foreach (var tile in tiles) GalleryGrid.Children.Add(tile);
        });
    }

    /// <summary>One picture of the widget (not focusable: the widget is). A video shows its frame and a play sign.</summary>
    private FrameworkElement GalleryTile(string path, GalleryCell cell, double corner, int width, Action opened)
    {
        var brush = new ImageBrush { Stretch = Stretch.UniformToFill };
        var tile = new Grid { CornerRadius = new CornerRadius(corner), Background = ThemeManager.Brush("GlyphCircleBrush") };
        tile.Children.Add(new Border { CornerRadius = new CornerRadius(corner), Background = brush });
        Grid.SetRow(tile, cell.Row);
        Grid.SetColumn(tile, cell.Column);
        Grid.SetRowSpan(tile, cell.RowSpan);
        Grid.SetColumnSpan(tile, cell.ColumnSpan);
        if (Gallery.IsVideo(path))
        {
            tile.Children.Add(new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(26), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x99, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Child = new FontIcon { Glyph = "\uE768", FontSize = 22, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), Margin = new Thickness(3, 0, 0, 0) }
            });
            _ = LoadVideoFrameAsync(path, brush, opened);
            return tile;
        }
        try
        {
            var picture = new BitmapImage { DecodePixelWidth = width, UriSource = new Uri(path) };
            picture.ImageOpened += (_, _) => opened();
            picture.ImageFailed += (_, args) => { AppLog.Write($"Galleria: {args.ErrorMessage}"); opened(); };
            brush.ImageSource = picture;
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException) { opened(); }
        return tile;
    }

    /// <summary>A video's frame for its tile, from the Windows thumbnail cache.</summary>
    private static async Task LoadVideoFrameAsync(string path, ImageBrush brush, Action opened)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView, 480);
            if (thumbnail is not null)
            {
                var picture = new BitmapImage();
                await picture.SetSourceAsync(thumbnail);
                brush.ImageSource = picture;
            }
        }
        catch (Exception ex) { AppLog.Write($"Galleria: anteprima video: {ex.Message}"); }
        finally { opened(); }
    }

    private void OnGalleryOpen(object sender, RoutedEventArgs args)
    {
        Sounds.Play(UiSound.Open);
        OpenGalleryViewer(_gallery.At(0));
    }

    // ───────── Gallery viewer ─────────

    private int _viewerIndex;

    private bool ViewerOpen => GalleryViewer.Visibility == Visibility.Visible;

    /// <summary>Every screenshot and video, one at a time over the whole screen, from the newest.</summary>
    private void OpenGalleryViewer(string? path)
    {
        if (_gallery.Count == 0 || path is null) return;
        _viewerIndex = Math.Max(0, _gallery.IndexOf(path));
        GalleryViewer.Visibility = Visibility.Visible;
        ElementCompositionPreview.GetElementVisual(GalleryViewer).Opacity = 0;
        Motion.Fade(GalleryViewer, 1, 200, _prefs.ReduceMotion);
        ShowViewerPicture();
        AutomationProperties.SetName(GalleryViewerClose, Strings.Get("Close"));
        (_gallery.Count > 1 ? GalleryViewerNext : GalleryViewerClose).Focus(FocusState.Keyboard);
    }

    private void StepGalleryViewer(int step)
    {
        if (!ViewerOpen || _gallery.Count < 2) return;
        Sounds.Play(UiSound.Move);
        _viewerIndex = ((_viewerIndex + step) % _gallery.Count + _gallery.Count) % _gallery.Count;
        ShowViewerPicture();
    }

    private void ShowViewerPicture()
    {
        if (_gallery.At(_viewerIndex) is not { } path) return;
        StopViewerVideo();
        if (Gallery.IsVideo(path))
        {
            // A video plays with its sound, so the home music steps aside meanwhile.
            HomeMusic.Set(false, DispatcherQueue);
            GalleryViewerImage.Source = null;
            GalleryViewerImage.Visibility = Visibility.Collapsed;
            GalleryViewerVideo.Visibility = Visibility.Visible;
            GalleryViewerVideo.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path));
        }
        else
        {
            GalleryViewerVideo.Visibility = Visibility.Collapsed;
            GalleryViewerImage.Visibility = Visibility.Visible;
            var width = (int)Math.Ceiling(1540 * Math.Max(1, Root.XamlRoot?.RasterizationScale ?? 1));
            try { GalleryViewerImage.Source = new BitmapImage { DecodePixelWidth = width, UriSource = new Uri(path) }; }
            catch (Exception ex) when (ex is UriFormatException or ArgumentException) { GalleryViewerImage.Source = null; }
        }
        GalleryViewerName.Text = Path.GetFileNameWithoutExtension(path);
        GalleryViewerCount.Text = $"{_viewerIndex + 1} / {_gallery.Count}";
        var several = _gallery.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        GalleryViewerPrevious.Visibility = GalleryViewerNext.Visibility = several;
        AutomationProperties.SetName(GalleryViewerPrevious, Strings.Get("GalleryPrevious"));
        AutomationProperties.SetName(GalleryViewerNext, Strings.Get("GalleryNext"));
    }

    private void StopViewerVideo()
    {
        if (GalleryViewerVideo.Source is null) return;
        GalleryViewerVideo.MediaPlayer?.Pause();
        (GalleryViewerVideo.Source as IDisposable)?.Dispose();
        GalleryViewerVideo.Source = null;
    }

    private void CloseGalleryViewer()
    {
        if (!ViewerOpen) return;
        Sounds.Play(UiSound.Back);
        StopViewerVideo();
        GalleryViewer.Visibility = Visibility.Collapsed;
        GalleryViewerImage.Source = null;
        UpdateMusic(IsForeground);
        GalleryOpen.Focus(FocusState.Keyboard);
    }

    private void OnGalleryClose(object sender, RoutedEventArgs args) => CloseGalleryViewer();

    /// <summary>A click outside the picture and the buttons closes the viewer.</summary>
    private void OnGalleryViewerTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args)
    {
        if (ReferenceEquals(args.OriginalSource, GalleryViewer)) CloseGalleryViewer();
    }

    private void OnGalleryPrevious(object sender, RoutedEventArgs args) => StepGalleryViewer(-1);

    private void OnGalleryNext(object sender, RoutedEventArgs args) => StepGalleryViewer(1);

    /// <summary>
    /// The controller around the gallery, which is one stop: up from the dock reaches it, up again the app bar; down or
    /// left goes back to the dock. Returns true when it took care of the move.
    /// </summary>
    private bool MoveAroundGallery(FocusNavigationDirection direction, DependencyObject focused)
    {
        if (ViewerOpen)
        {
            if (direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right)
                StepGalleryViewer(direction == FocusNavigationDirection.Left ? -1 : 1);
            return true;
        }
        var gallery = WidgetColumn.Visibility == Visibility.Visible && GalleryWidget.Visibility == Visibility.Visible && GalleryOpen.IsEnabled;
        var inBar = IsInside(focused, TopBar);
        if (gallery && IsInside(focused, GalleryOpen))
        {
            if (direction == FocusNavigationDirection.Up) FocusTopBar();
            else if (direction != FocusNavigationDirection.Right) FocusSelection();
            return true;
        }
        if (direction == FocusNavigationDirection.Up && IsInside(focused, DockRow))
        {
            if (gallery) GalleryOpen.Focus(FocusState.Keyboard);
            else FocusTopBar();
            return true;
        }
        if (inBar && direction == FocusNavigationDirection.Down)
        {
            if (gallery) GalleryOpen.Focus(FocusState.Keyboard);
            else FocusSelection();
            return true;
        }
        if (inBar && direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right)
            return FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = TopBar }) || true;
        return inBar && direction == FocusNavigationDirection.Up;
    }

    /// <summary>The app bar's first button (it opens as soon as the focus is inside it).</summary>
    private void FocusTopBar()
    {
        if (FocusManager.FindFirstFocusableElement(TopBar) is Control control) control.Focus(FocusState.Keyboard);
    }
    // ───────── Pieces ─────────

    private static TextBlock WidgetHeader(string text) => new()
    {
        Text = text.ToUpper(CultureInfo.CurrentCulture), FontSize = 17, FontWeight = FontWeights.Bold, CharacterSpacing = 60,
        TextTrimming = TextTrimming.CharacterEllipsis, Foreground = ThemeManager.Brush("TextSecondaryBrush")
    };

    private static FrameworkElement WidgetMessage(string titleKey, string message)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(WidgetHeader(Strings.Get(titleKey)));
        panel.Children.Add(new TextBlock
        {
            Text = message, FontSize = 21, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis, Foreground = ThemeManager.Brush("TextSecondaryBrush")
        });
        return panel;
    }
}
