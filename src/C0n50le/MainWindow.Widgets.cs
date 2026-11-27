using System.Globalization;
using C0n50le.Core.Localization;
using C0n50le.Core.Widgets;
using C0n50le.Services;
using C0n50le.Ui;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
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
    private bool _galleryFront;

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
        _galleryTimer.Tick += (_, _) => ShowNextPicture();
        ElementCompositionPreview.GetElementVisual(GalleryA).Opacity = 0;
        ElementCompositionPreview.GetElementVisual(GalleryB).Opacity = 0;
        GalleryA.Opacity = GalleryB.Opacity = 1;
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

    private async Task LoadGalleryAsync()
    {
        var folder = GalleryFolder;
        _galleryLoaded = folder;
        GalleryMessage.Text = "";
        GallerySkeleton.Visibility = Visibility.Visible;
        StartPulse(GallerySkeleton);
        _gallery = await Task.Run(() => Gallery.Load(folder));
        if (_galleryLoaded != folder) return;
        GalleryMessage.Text = _gallery.Count == 0 ? Strings.Get("GalleryEmpty") : "";
        if (_gallery.Count == 0) HideGallerySkeleton();
        if (_gallery.Count == 0)
        {
            Motion.Fade(GalleryA, 0, 300, _prefs.ReduceMotion);
            Motion.Fade(GalleryB, 0, 300, _prefs.ReduceMotion);
        }
        ShowNextPicture();
    }

    /// <summary>Cross-fades to the next picture; a file that will not open is skipped at the next turn.</summary>
    private void ShowNextPicture()
    {
        if (!_prefs.ShowWidgets || !_prefs.ShowGallery || !AppWindow.IsVisible || HomePage.Visibility != Visibility.Visible || _gallery.Next() is not { } path) return;
        var width = (int)Math.Ceiling(Math.Max(460, GalleryWidget.ActualWidth) * Math.Max(1, Root.XamlRoot?.RasterizationScale ?? 1));
        BitmapImage picture;
        try { picture = new BitmapImage { DecodePixelWidth = width, UriSource = new Uri(path) }; }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException) { return; }
        var front = _galleryFront ? GalleryA : GalleryB;
        var back = _galleryFront ? GalleryB : GalleryA;
        _galleryFront = !_galleryFront;
        picture.ImageOpened += (_, _) =>
        {
            HideGallerySkeleton();
            Motion.Fade(front, 1, 700, _prefs.ReduceMotion);
            Motion.Fade(back, 0, 700, _prefs.ReduceMotion);
        };
        picture.ImageFailed += (_, args) => AppLog.Write($"Galleria: {args.ErrorMessage}");
        front.Source = picture;
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
