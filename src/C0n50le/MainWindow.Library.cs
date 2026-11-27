using System.Numerics;
using C0n50le.Core.Audio;
using C0n50le.Core.Catalog;
using C0n50le.Core.Library;
using C0n50le.Core.Localization;
using C0n50le.Services;
using C0n50le.Ui;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace C0n50le;

/// <summary>One tile of the home: a game, or Add / Settings at the end.</summary>
internal sealed record Tile(Button Card, Game? Game);

public sealed partial class MainWindow
{
    /// <summary>tvOS grows a focused card from 250×150 to 306×184.</summary>
    private const float CardFocusScale = 1.224f;

    private readonly List<Tile> _tiles = [];
    private readonly HashSet<string> _fetched = [];
    private readonly DescriptionFinder _descriptions = new(Updater.CurrentVersion);
    private readonly HashSet<string> _descriptionRequests = [];
    private readonly CancellationTokenSource _descriptionLifetime = new();
    private List<Game> _games = [];
    private List<Game> _detected = [];
    private Tile? _addTile, _systemTile;
    private int _selected = -1;
    private bool _scanning, _loadedOnce;
    private GameSession? _session;
    private DispatcherQueueTimer? _sessionTimer;
    private CancellationTokenSource? _artworkLookup;
    private Image? _frontBackdrop;
    private string? _backdropPath;
    private int _backdropRequest, _paletteRequest;

    private Game? SelectedGame => _selected >= 0 && _selected < _games.Count ? _games[_selected] : null;

    private void InitializeLibrary()
    {
        _sessionTimer = DispatcherQueue.CreateTimer();
        _sessionTimer.Interval = TimeSpan.FromMilliseconds(750);
        _sessionTimer.Tick += (_, _) => PollSession();
        Motion.Reduce = _prefs.ReduceMotion;
        ElementCompositionPreview.GetElementVisual(Toast).Opacity = 0;
        // The lifted card casts its tvOS shadow on the page behind it.
        _shadow.Receivers.Add(ShelfFade);
    }

    /// <summary>Scans the PC and rebuilds the home, keeping the selection.</summary>
    private async Task LoadLibraryAsync(bool firstLoad = false, string? select = null)
    {
        if (_scanning) return;
        _scanning = true;
        try
        {
            var selection = select ?? SelectedGame?.Id ?? _prefs.LastPlayed;
            var roots = DiscoveryRoots.FromSystem(_prefs.GamesFolder, _prefs.ScanSteam, _prefs.ScanEpic);
            _detected = await Task.Run(() => GameDiscovery.Scan(roots));
            _games = LibraryOrder.Build(_prefs, _detected);
            _selected = _games.FindIndex(game => game.Id == selection);
            if (_selected < 0 && _games.Count > 0) _selected = 0;
            RenderCards();
            RefreshHero();
            if (AppWindow.IsVisible && !IsOverlayOpen && !PageOpen && !IntroVisible) FocusSelection();
            AppLog.Write($"Libreria: {_games.Count} giochi ({_detected.Count} trovati)");
            if (firstLoad)
            {
                _loadedOnce = true;
                if (_prefs.PruneArt(_paths.ArtFolder) is > 0 and var removed) AppLog.Write($"Immagini rimosse: {removed}");
            }
            _ = PrefetchArtworkAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Libreria: {ex}");
            ShowToast(Strings.Format("LibraryFailed", ex.Message));
        }
        finally { _scanning = false; }
    }

    /// <summary>The dock: every game, the most recent first, then Add and Settings.</summary>
    private void RenderCards()
    {
        if (DockRow is null) return;
        DockRow.Children.Clear();
        _tiles.Clear();
        for (var index = 0; index < _games.Count; index++)
        {
            var tile = GameTile(index);
            _tiles.Add(tile);
            DockRow.Children.Add(Host(tile));
        }
        _addTile = ActionTile("", Strings.Get("AddGame"), Strings.Get("AddGameBody"), async () => await AddGameAsync());
        _systemTile = ActionTile("", Strings.Get("System"), Strings.Get("SystemCardBody"), OpenSystem);
        DockRow.Children.Add(Host(_addTile));
        if (_prefs.ShowSystemCard) DockRow.Children.Add(Host(_systemTile));
        UpdateCards();
    }

    /// <summary>A card and the title that appears under it when focused, like tvOS.</summary>
    private static Grid Host(Tile tile)
    {
        // A fixed slot: the card grows over it on focus without moving its neighbours.
        var host = new Grid { Width = 250, Height = 150, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(tile.Card);
        return host;
    }

    private Tile GameTile(int index)
    {
        var game = _games[index];
        var card = new Button { Style = ThemeManager.Style("TvCard") };
        var tile = new Tile(card, game);
        AddFocusLift(tile);
        card.Click += (_, _) =>
        {
            Select(index);
            StartOrResume();
        };
        card.RightTapped += (_, args) =>
        {
            Select(index);
            OpenCardMenu(card);
            args.Handled = true;
        };
        card.GotFocus += (_, _) =>
        {
            Select(index);
            BringRowIntoView(card);
        };
        return tile;
    }

    private Tile ActionTile(string glyph, string label, string description, Action action)
    {
        var card = new Button
        {
            Style = ThemeManager.Style("TvCard"), Background = ThemeManager.Brush("MaterialBrush"),
            Content = new FontIcon { Glyph = glyph, FontSize = 52, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        AutomationProperties.SetName(card, label);
        var tile = new Tile(card, null);
        AddFocusLift(tile);
        card.Click += (_, _) => { Sounds.Play(UiSound.Confirm); action(); };
        card.GotFocus += (_, _) =>
        {
            ShowActionHero(label, description);
            BringRowIntoView(card);
        };
        return tile;
    }

    /// <summary>The tvOS focus on a card: it grows by 22% with a soft lifted shadow, and its title appears below.</summary>
    private void AddFocusLift(Tile tile)
    {
        var card = tile.Card;
        card.Shadow = _shadow;
        void Update(bool focused)
        {
            var visual = ElementCompositionPreview.GetElementVisual(card);
            visual.CenterPoint = new Vector3((float)card.Width / 2, (float)card.Height / 2, 0);
            ElementCompositionPreview.SetIsTranslationEnabled(card, true);
            var scale = focused ? CardFocusScale : 1f;
            var lift = new Vector3(0, 0, focused ? 48 : 0);
            if (!Motion.Enabled(Motion.Reduce))
            {
                visual.Scale = new Vector3(scale, scale, 1);
                card.Translation = lift;
            }
            else
            {
                var spring = visual.Compositor.CreateSpringVector3Animation();
                spring.FinalValue = new Vector3(scale, scale, 1);
                spring.DampingRatio = 0.72f;
                spring.Period = TimeSpan.FromMilliseconds(45);
                visual.StartAnimation("Scale", spring);
                card.Translation = lift;
            }
        }
        card.GotFocus += (_, _) => Update(true);
        card.LostFocus += (_, _) => Update(false);
    }

    private readonly ThemeShadow _shadow = new();

    /// <summary>Keeps the focused card in view when the dock holds more than fits, sliding it towards the middle.</summary>
    private void BringRowIntoView(FrameworkElement card)
    {
        if (card.Parent is not FrameworkElement host) return;
        var viewport = DockScroll.ViewportWidth;
        if (viewport <= 0) return;
        // Where the card sits in the row, padding included, and room for its grown size and title on both sides.
        var x = host.TransformToVisual(DockRow).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
        const double margin = 96;
        var left = DockScroll.HorizontalOffset;
        double? target = null;
        if (x - margin < left) target = x - margin;
        else if (x + host.ActualWidth + margin > left + viewport) target = x + host.ActualWidth + margin - viewport;
        if (target is { } offset)
            DockScroll.ChangeView(Math.Clamp(offset, 0, Math.Max(0, DockScroll.ScrollableWidth)), null, null, _prefs.ReduceMotion);
    }

    /// <summary>Pictures on the cards, and the running badge.</summary>
    private void UpdateCards()
    {
        foreach (var tile in _tiles)
        {
            if (tile.Game is not { } game) continue;
            var art = _prefs.Art.GetValueOrDefault(game.Id);
            var title = LibraryOrder.DisplayTitle(game, art);
            var running = _session?.Game.Id == game.Id;
            AutomationProperties.SetName(tile.Card, title);
            var key = string.Join("|", title, art?.Background, art?.Logo, art?.Banner, art?.Card, art?.CardImage, running);
            if (tile.Card.Tag is string previous && previous == key) continue;
            tile.Card.Tag = key;
            tile.Card.Content = CardContent(title, art, running);
        }
    }

    /// <summary>
    /// A wide card like a tvOS app icon: the game's background with its transparent logo on top; else its wide banner;
    /// else its cover; else its name.
    /// </summary>
    private static Grid CardContent(string title, Artwork? art, bool running)
    {
        var grid = new Grid { CornerRadius = new CornerRadius(30) };
        var media = art is { HasCatalogMedia: true } ? art : null;
        // The picture chosen in the detail page wins over the automatic composition.
        if (LoadImage(art?.CardImage, 640) is { } chosen)
            grid.Children.Add(new Image { Source = chosen, Stretch = Stretch.UniformToFill });
        else if (LoadImage(media?.Background, 640) is { } background && File.Exists(media!.Logo))
        {
            grid.Children.Add(new Image { Source = background, Stretch = Stretch.UniformToFill });
            grid.Children.Add(new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 0, 0, 0)) });
            grid.Children.Add(new Image
            {
                Source = LoadImage(media.Logo, 480), Stretch = Stretch.Uniform, Margin = new Thickness(26, 20, 26, 20),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
        }
        else if (LoadImage(media?.Banner, 640) is { } banner)
            grid.Children.Add(new Image { Source = banner, Stretch = Stretch.UniformToFill });
        else if (LoadImage(media?.Card, 480) is { } cover)
            grid.Children.Add(new Image { Source = cover, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top });
        else if (LoadImage(media?.Background, 640) is { } wide)
        {
            grid.Children.Add(new Image { Source = wide, Stretch = Stretch.UniformToFill });
            grid.Children.Add(TitleOnArt(title));
        }
        else
            grid.Children.Add(new TextBlock
            {
                Text = title, FontSize = 27, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(20, 0, 20, 0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
        if (running)
            grid.Children.Add(new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Margin = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 0, 0, 0)), BorderThickness = new Thickness(2)
            });
        return grid;
    }

    private static Grid TitleOnArt(string title)
    {
        var veil = new Grid();
        var gradient = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0, 0, 0, 0), Offset = 0.3 });
        gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(190, 0, 0, 0), Offset = 1 });
        veil.Background = gradient;
        veil.Children.Add(new TextBlock
        {
            Text = title, FontSize = 25, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Margin = new Thickness(20, 0, 20, 16),
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        });
        return veil;
    }

    private static BitmapImage? LoadImage(string? path, int width)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return new BitmapImage(new Uri(path)) { DecodePixelWidth = width };
    }

    private bool _heroShowsAction;

    private void Select(int index)
    {
        if (index < 0 || index >= _games.Count || (index == _selected && !_heroShowsAction)) return;
        _selected = index;
        RefreshHero();
    }

    /// <summary>Add and Settings have no artwork: the top shelf shows what they do, over the last game's picture.</summary>
    private void ShowActionHero(string title, string description)
    {
        _heroShowsAction = true;
        HeroLogo.Visibility = Visibility.Collapsed;
        HeroTitleText.Visibility = Visibility.Visible;
        Home.HeroTitle = title;
        Home.HeroDetails = "";
        Home.HeroDescription = description;
        Motion.Entrance(Hero, _prefs.ReduceMotion);
    }

    private void FocusSelection()
    {
        var state = FocusState.Keyboard;
        var tile = _selected >= 0 && _selected < _tiles.Count ? _tiles[_selected] : _addTile;
        tile?.Card.Focus(state);
    }

    /// <summary>The top shelf, the backdrop and the ambient colour for the selected game.</summary>
    private void RefreshHero(bool fetch = true)
    {
        _heroShowsAction = false;
        RefreshGameHero(fetch);
    }

    private void RefreshGameHero(bool fetch)
    {
        var game = SelectedGame;
        var art = game is null ? null : _prefs.Art.GetValueOrDefault(game.Id);
        var media = art is { HasCatalogMedia: true } ? art : null;
        Home.HasSelection = game is not null;
        Home.HeroTitle = game is null ? Strings.Get("EmptyTitle") : LibraryOrder.DisplayTitle(game, art);
        var details = new List<string>();
        if (game is not null) details.Add(SourceLabel(game.Source));
        if (art?.IsMatched == true) details.AddRange(art.Details);
        Home.HeroDetails = string.Join("  ·  ", details);
        RefreshDescription(game);
        var running = game is not null && _session?.Game.Id == game.Id;
        Home.IsRunning = _session is not null;
        Home.HeroStatus = running ? Strings.Get("RunningResume")
            : _session is not null ? Strings.Format("OtherRunning", LibraryOrder.DisplayTitle(_session.Game, _prefs.Art.GetValueOrDefault(_session.Game.Id))) : "";
        // The game's logo in the top shelf when there is one, its name otherwise, like a tvOS product screen.
        var logo = LoadImage(media?.Logo, 1200);
        HeroLogo.Source = logo;
        HeroLogo.Visibility = logo is null ? Visibility.Collapsed : Visibility.Visible;
        HeroTitleText.Visibility = logo is null ? Visibility.Visible : Visibility.Collapsed;
        // One or more backgrounds: the first now, the others in turn while the game stays selected.
        _heroBackgrounds = art?.BackgroundsToShow(File.Exists) ?? [];
        _heroBackground = 0;
        var background = _heroBackgrounds.Count > 0 ? _heroBackgrounds[0] : null;
        ShowBackdrop(background);
        RestartBackdropRotation();
        _ = UpdatePaletteAsync(background ?? media?.Card);
        Motion.Entrance(Hero, _prefs.ReduceMotion);
        UpdateCards();
        if (fetch) ScheduleArtwork(game);
    }

    private static string DescriptionTitle(Game game, Artwork? art) =>
        art is { IsMatched: true, CatalogTitle.Length: > 0 } ? art.CatalogTitle : LibraryOrder.SearchTitle(game, art);

    private void RefreshDescription(Game? game)
    {
        Home.HeroDescription = game is null ? Strings.Get("EmptyBody") : "";
        if (game is null) return;
        var art = _prefs.ArtFor(game);
        var language = Strings.Language;
        var title = DescriptionTitle(game, art);
        var key = language + "|" + title;
        if (art.Descriptions.TryGetValue(key, out var cached))
        {
            Home.HeroDescription = cached.Text;
            return;
        }
        if (language == Strings.English && art.IsMatched) Home.HeroDescription = art.Description;
        if (_descriptionRequests.Add(game.Id + "|" + key))
            _ = FetchDescriptionAsync(game, title, language, key);
    }

    private async Task FetchDescriptionAsync(Game game, string title, string language, string key)
    {
        try
        {
            await Task.Delay(350, _descriptionLifetime.Token);
            if (SelectedGame?.Id != game.Id || _heroShowsAction || Strings.Language != language)
            {
                _descriptionRequests.Remove(game.Id + "|" + key);
                return;
            }
            var result = await _descriptions.FindDescriptionAsync(title, game.SteamId, language, _descriptionLifetime.Token);
            if (_exiting || result.Text.Length == 0) return;
            _prefs.ArtFor(game).Descriptions[key] = result;
            Save();
            // A late reply must not replace the currently selected game's text or a menu description.
            if (SelectedGame?.Id == game.Id && !_heroShowsAction && Strings.Language == language
                && DescriptionTitle(game, _prefs.ArtFor(game)) == title)
                RefreshDescription(game);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write($"Descrizione: {ex.Message}"); }
    }

    private static string SourceLabel(string source) => source switch
    {
        GameSource.Steam => "Steam",
        GameSource.Epic => "Epic Games",
        GameSource.Folder => Strings.Get("SourceFolder"),
        _ => Strings.Get("SourceManual")
    };

    private async Task UpdatePaletteAsync(string? path)
    {
        var request = ++_paletteRequest;
        var color = await ArtworkPalette.ReadAsync(path);
        if (request != _paletteRequest || _exiting) return;
        ThemeManager.UseArtwork(color);
    }

    private IReadOnlyList<string> _heroBackgrounds = [];
    private int _heroBackground;
    private DispatcherQueueTimer? _backdropTimer;

    private void RestartBackdropRotation()
    {
        _backdropTimer ??= CreateBackdropTimer();
        _backdropTimer.Stop();
        if (_heroBackgrounds.Count > 1) _backdropTimer.Start();
    }

    private DispatcherQueueTimer CreateBackdropTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(10);
        timer.Tick += (_, _) =>
        {
            if (_heroBackgrounds.Count < 2 || _heroShowsAction || !AppWindow.IsVisible || PageOpen) return;
            _heroBackground = (_heroBackground + 1) % _heroBackgrounds.Count;
            ShowBackdrop(_heroBackgrounds[_heroBackground]);
        };
        return timer;
    }

    private void ShowBackdrop(string? path)
    {
        if (_backdropPath == path) return;
        _backdropPath = path;
        var request = ++_backdropRequest;
        var old = _frontBackdrop;
        if (path is null)
        {
            if (old is not null) Motion.Fade(old, 0, 420, _prefs.ReduceMotion);
            _frontBackdrop = null;
            return;
        }
        var layer = old == BackdropA ? BackdropB : BackdropA;
        layer.Source = null;
        ElementCompositionPreview.GetElementVisual(layer).Opacity = 0;
        layer.Opacity = 1;
        var bitmap = LoadImage(path, 1920);
        if (bitmap is null) return;
        void Arrived()
        {
            if (_backdropRequest != request || _exiting) return;
            _frontBackdrop = layer;
            Motion.Fade(layer, 1f, 600, _prefs.ReduceMotion);
            if (old is not null && old != layer) Motion.Fade(old, 0, 600, _prefs.ReduceMotion);
        }
        bitmap.ImageOpened += (_, _) => Arrived();
        bitmap.ImageFailed += (_, _) =>
        {
            if (_backdropRequest == request && old is not null) Motion.Fade(old, 0, 320, _prefs.ReduceMotion);
        };
        layer.Source = bitmap;
    }

    /// <summary>Starts the selected game, or brings it back when it is already running.</summary>
    private void StartOrResume()
    {
        var game = SelectedGame;
        if (game is null || IsOverlayOpen) return;
        Sounds.Play(UiSound.Confirm);
        if (_session is not null)
        {
            if (_session.Game.Id != game.Id)
            {
                ShowToast(Strings.Format("CloseOtherFirst", LibraryOrder.DisplayTitle(_session.Game, _prefs.Art.GetValueOrDefault(_session.Game.Id))));
                _selected = _games.FindIndex(entry => entry.Id == _session.Game.Id);
                RefreshHero(fetch: false);
                FocusSelection();
                return;
            }
            if (!_session.Resume()) ShowToast(Strings.Get("WindowNotReady"));
            return;
        }
        try
        {
            _session = new GameSession(game, _prefs.Watched.GetValueOrDefault(game.Id, game.WatchProcess));
            _prefs.MarkPlayed(game, DateTimeOffset.Now);
            _artworkLookup?.Cancel();
            Save();
            _sessionTimer?.Start();
            RefreshHero(fetch: false);
            HomeMusic.Set(false, DispatcherQueue);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Avvio non riuscito: {ex.Message}");
            ShowToast(Strings.Format("LaunchFailed", ex.Message));
        }
    }

    private void PollSession()
    {
        if (_session is null) return;
        try { if (_session.Poll()) return; }
        catch (Exception ex) { AppLog.Write($"Sessione: {ex.Message}"); }
        var game = _session.Game;
        var timedOut = _session.TimedOut;
        _sessionTimer?.Stop();
        _session.Dispose();
        _session = null;
        AppLog.Write(timedOut ? "Finestra del gioco non trovata" : "Gioco chiuso");
        // The game that just closed moves to the front of the dock, like on a console.
        _games = LibraryOrder.Build(_prefs, _detected);
        _selected = _games.FindIndex(entry => entry.Id == game.Id);
        RenderCards();
        RefreshHero(fetch: false);
        if (timedOut) ShowToast(Strings.Get("WindowNotFound"));
        ShowHome();
        TryPromptPendingUpdate();
    }

    private void CloseRunningGame()
    {
        if (_session is null) return;
        if (!_session.RequestClose()) ShowToast(Strings.Get("CloseFailed"));
    }

    // ───────── Covers, logos and descriptions from the catalog ─────────

    private void ScheduleArtwork(Game? game)
    {
        if (game is null || _session is not null) return;
        var art = _prefs.Art.GetValueOrDefault(game.Id);
        if (art is { NeedsMedia: false } || _fetched.Contains(game.Id)) return;
        _artworkLookup?.Cancel();
        var lookup = _artworkLookup = new CancellationTokenSource();
        _ = AutoArtworkAsync(game, lookup.Token, delay: 600);
    }

    /// <summary>After a scan, looks up every game without pictures (or with older ones), one at a time.</summary>
    private async Task PrefetchArtworkAsync()
    {
        foreach (var game in _games.ToList())
        {
            if (_exiting || _session is not null) return;
            var art = _prefs.Art.GetValueOrDefault(game.Id);
            if (art is { NeedsMedia: false } || _fetched.Contains(game.Id)) continue;
            await AutoArtworkAsync(game, CancellationToken.None, delay: 250);
        }
    }

    private async Task AutoArtworkAsync(Game game, CancellationToken token, int delay)
    {
        try
        {
            await Task.Delay(delay, token);
            if (!_fetched.Add(game.Id)) return;
            var art = _prefs.ArtFor(game);
            var title = LibraryOrder.SearchTitle(game, art);
            CatalogMatch? match;
            if (art.IsMatched && Uri.TryCreate(art.CatalogUrl, UriKind.Absolute, out var page) && LaunchBoxParser.IsDetailsPage(page))
                match = new CatalogMatch(art.CatalogTitle.Length > 0 ? art.CatalogTitle : title, page);
            else match = LaunchBoxParser.PickAutomatic(await _catalog.SearchAsync(title, token), title);
            if (match is null)
            {
                AppLog.Write($"Copertina: nessun abbinamento certo ({game.Source})");
                return;
            }
            await ApplyCatalogAsync(game, match, token);
        }
        catch (OperationCanceledException) { _fetched.Remove(game.Id); }
        catch (Exception ex) { AppLog.Write($"Copertina: {ex.Message}"); }
    }

    /// <summary>Downloads the entry's pictures and saves them with its texts as the game's artwork.</summary>
    private async Task ApplyCatalogAsync(Game game, CatalogMatch match, CancellationToken token)
    {
        var entry = await _catalog.GetAsync(match, Strings.Language, token);
        var files = await DownloadAllAsync(entry, token);
        try
        {
            token.ThrowIfCancellationRequested();
            ApplyArtwork(game, match, entry, files);
        }
        finally { files.Dispose(); }
    }

    /// <summary>The pictures of an entry, downloaded to temporary files (deleted on dispose).</summary>
    internal sealed record CatalogFiles(string? Cover, string? Background, string? Logo, string? Banner) : IDisposable
    {
        public void Dispose()
        {
            foreach (var file in new[] { Cover, Background, Logo, Banner })
                if (file is not null && File.Exists(file)) File.Delete(file);
        }
    }

    private async Task<CatalogFiles> DownloadAllAsync(CatalogEntry entry, CancellationToken token)
    {
        var cover = TryDownload(entry.Cover, token);
        var background = TryDownload(entry.Background, token);
        var logo = TryDownload(entry.Logo, token);
        var banner = TryDownload(entry.Banner, token);
        await Task.WhenAll(cover, background, logo, banner);
        return new CatalogFiles(await cover, await background, await logo, await banner);
    }

    private async Task<string?> TryDownload(Uri? uri, CancellationToken token)
    {
        try { return await _catalog.DownloadAsync(uri, token); }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!token.IsCancellationRequested) { return null; }
    }

    private void ApplyArtwork(Game game, CatalogMatch match, CatalogEntry entry, CatalogFiles files)
    {
        var art = _prefs.ArtFor(game);
        // Another title: the pictures chosen for the previous one no longer belong to this game.
        if (art.CatalogId != match.Id)
        {
            art.CardImage = "";
            art.Backgrounds = [];
        }
        string Store(string? file, string kind) =>
            file is not null ? Core.Settings.Preferences.StoreImage(_paths.ArtFolder, game.Id, kind, file) : "";
        art.Card = Store(files.Cover, "card");
        art.Background = Store(files.Background, "background");
        art.Logo = Store(files.Logo, "logo");
        art.Banner = Store(files.Banner, "banner");
        art.CoverAspect = art.Card.Length > 0 ? entry.CoverAspect : 0;
        art.HasCatalogMedia = true;
        art.MediaVersion = Artwork.CurrentMediaVersion;
        art.Description = entry.Description;
        art.CatalogId = match.Id;
        art.CatalogTitle = match.Title;
        art.CatalogUrl = match.Page.AbsoluteUri;
        art.Details = entry.Details;
        Save();
        AppLog.Write($"Copertina applicata: {match.Id}");
        UpdateCards();
        if (SelectedGame?.Id == game.Id)
        {
            _backdropPath = null;
            RefreshHero(fetch: false);
        }
    }
}

/// <summary>The small animations of the interface, all skipped with "reduce motion" or Windows' animations off.</summary>
internal static class Motion
{
    private static readonly Windows.UI.ViewManagement.UISettings Settings = new();

    public static bool Enabled(bool reduce) => !reduce && Settings.AnimationsEnabled;

    public static bool Reduce { get; set; }

    public static void Fade(UIElement element, float target, int milliseconds, bool reduce)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!Enabled(reduce))
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = target;
            return;
        }
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, target);
        animation.Duration = TimeSpan.FromMilliseconds(milliseconds);
        visual.StartAnimation("Opacity", animation);
    }

    /// <summary>The top shelf fades in and rises a little when the focus moves to another game.</summary>
    public static void Entrance(UIElement element, bool reduce)
    {
        if (!Enabled(reduce)) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var compositor = visual.Compositor;
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, new Vector3(0, 12, 0));
        slide.InsertKeyFrame(1, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(new Vector2(.18f, .8f), new Vector2(.29f, 1)));
        slide.Duration = TimeSpan.FromMilliseconds(300);
        visual.StartAnimation("Translation", slide);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0.4f);
        fade.InsertKeyFrame(1, 1f);
        fade.Duration = TimeSpan.FromMilliseconds(300);
        visual.StartAnimation("Opacity", fade);
    }
}
