using System.Numerics;
using C0n50le.Core.Audio;
using C0n50le.Core.Catalog;
using C0n50le.Core.Library;
using C0n50le.Core.Localization;
using C0n50le.Services;
using C0n50le.Ui;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace C0n50le;

public sealed partial class MainWindow
{
    private const double CardSize = 280;
    private readonly Dictionary<Button, Button> _moreButtons = [];
    private bool _tileFocused;
    private readonly List<Button> _cards = [];
    private readonly HashSet<string> _fetched = [];
    private List<Game> _games = [];
    private List<Game> _detected = [];
    private Button? _addCard, _systemCard;
    private int _selected = -1;
    private bool _scanning;
    private GameSession? _session;
    private DispatcherQueueTimer? _sessionTimer, _hoverTimer;
    private int _hoverIndex = -1;
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
        _hoverTimer = DispatcherQueue.CreateTimer();
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(140);
        _hoverTimer.IsRepeating = false;
        _hoverTimer.Tick += (_, _) => Select(_hoverIndex, scroll: false);
        foreach (var button in new[] { SystemPill, DesktopPill, SystemBack })
            Motion.AddLift(button, 1.04);
        Motion.Reduce = _prefs.ReduceMotion;
        ElementCompositionPreview.GetElementVisual(Toast).Opacity = 0;
    }

    /// <summary>Scans the PC and rebuilds the row, keeping the selection.</summary>
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
            if (AppWindow.IsVisible && !IsOverlayOpen && SystemPage.Visibility == Visibility.Collapsed && !IntroVisible) FocusSelection();
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

    private bool _loadedOnce;

    private void RenderCards()
    {
        if (Cards is null) return;
        _hoverTimer?.Stop();
        Cards.Children.Clear();
        _cards.Clear();
        _moreButtons.Clear();
        for (var index = 0; index < _games.Count; index++)
        {
            var position = index;
            var card = new Button { Style = ThemeManager.Style("GameCard") };
            Motion.AddLift(card, 1.06, lift: 10);
            card.PointerEntered += (_, args) =>
            {
                if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
                _hoverIndex = position;
                _hoverTimer?.Start();
            };
            card.PointerExited += (_, _) =>
            {
                if (_hoverIndex == position) _hoverTimer?.Stop();
            };
            card.Click += (_, _) =>
            {
                _hoverTimer?.Stop();
                Select(position);
                StartOrResume();
            };
            card.RightTapped += (_, args) =>
            {
                Select(position);
                OpenCardMenu(card);
                args.Handled = true;
            };
            card.GotFocus += (_, _) => Select(position);
            // The card's options open from a small ⋯ on the card itself (mouse), from Y (controller) or a right-click.
            var more = new Button
            {
                Style = ThemeManager.Style("IconButton"), Width = 44, Height = 44, IsTabStop = false, AllowFocusOnInteraction = false,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 8, 10, 14)), Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12),
                Content = new FontIcon { Glyph = "", FontSize = 18 }, Visibility = Visibility.Collapsed
            };
            AutomationProperties.SetName(more, Strings.Get("HintOptions"));
            ToolTipService.SetToolTip(more, Strings.Get("HintOptions"));
            more.Click += (_, _) =>
            {
                Select(position);
                OpenCardMenu(card);
            };
            var frame = new Grid();
            frame.Children.Add(new Grid());
            frame.Children.Add(more);
            card.Content = frame;
            _moreButtons[card] = more;
            _cards.Add(card);
            Cards.Children.Add(card);
        }
        _addCard = TileButton("", Strings.Get("AddGame"), async () => await AddGameAsync());
        Cards.Children.Add(_addCard);
        _systemCard = TileButton("", Strings.Get("System"), OpenSystem);
        _systemCard.Visibility = _prefs.ShowSystemCard ? Visibility.Visible : Visibility.Collapsed;
        Cards.Children.Add(_systemCard);
        UpdateCards();
    }

    /// <summary>The square cards at the end of the row: add a game, System.</summary>
    private Button TileButton(string glyph, string label, Action action)
    {
        var content = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 40, Foreground = ThemeManager.Brush("AccentBrush") });
        content.Children.Add(new TextBlock
        {
            Text = label, FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 150, Foreground = ThemeManager.Brush("TextBrush")
        });
        var button = new Button
        {
            Style = ThemeManager.Style("GameCard"), Background = ThemeManager.Brush("SurfaceBrush"),
            Content = content, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(button, label);
        Motion.AddLift(button, 1.06, lift: 10);
        button.Click += (_, _) => { Sounds.Play(UiSound.Confirm); action(); };
        button.GotFocus += (_, _) =>
        {
            _hoverTimer?.Stop();
            _tileFocused = true;
            button.BorderBrush = ThemeManager.Brush("FocusBrush");
            button.BorderThickness = new Thickness(3);
            UpdateCards();
        };
        button.LostFocus += (_, _) =>
        {
            _tileFocused = false;
            button.BorderThickness = new Thickness(0);
            UpdateCards();
        };
        return button;
    }

    /// <summary>Card sizes, pictures and the selection edge.</summary>
    private void UpdateCards()
    {
        var edge = ThemeManager.Brush("FocusBrush");
        for (var index = 0; index < _cards.Count; index++)
        {
            var card = _cards[index];
            var game = _games[index];
            var art = _prefs.Art.GetValueOrDefault(game.Id);
            var title = LibraryOrder.DisplayTitle(game, art);
            var hasCover = art is { HasCatalogMedia: true } && File.Exists(art.Card);
            var selected = index == _selected && !_tileFocused;
            card.BorderThickness = new Thickness(selected ? 3 : 0);
            card.BorderBrush = selected ? edge : null;
            card.Opacity = selected ? 1 : 0.86;
            if (_moreButtons.TryGetValue(card, out var more))
                more.Visibility = selected && _input.Mode == Core.Input.InputMode.MouseKeyboard ? Visibility.Visible : Visibility.Collapsed;
            var running = _session?.Game.Id == game.Id;
            var path = hasCover ? art!.Card : "";
            var key = (title, path, running);
            AutomationProperties.SetName(card, title);
            if (card.Tag is ValueTuple<string, string, bool> previous && previous == key) continue;
            card.Tag = key;
            if (card.Content is Grid frame && frame.Children.Count > 0) frame.Children[0] = CardContent(title, path, running);
        }
    }

    private static Grid CardContent(string title, string cover, bool running)
    {
        var grid = new Grid();
        Corners.SetSize(grid, CornerSize.Large);
        // Square cards: the box art fills the square from the top, where the title usually is.
        if (LoadImage(cover, 480) is { } image)
            grid.Children.Add(new Image { Source = image, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top });
        else
        {
            var placeholder = new StackPanel { Spacing = 18, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(18, 0, 18, 0) };
            placeholder.Children.Add(new FontIcon { Glyph = "", FontSize = 40, Foreground = ThemeManager.Brush("TextMutedBrush") });
            placeholder.Children.Add(new TextBlock
            {
                Text = title, FontSize = 21, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ThemeManager.Brush("TextBrush")
            });
            grid.Children.Add(placeholder);
        }
        if (running)
        {
            var badge = new Border
            {
                Background = ThemeManager.Brush("AccentBrush"), Padding = new Thickness(12, 0, 12, 0), Height = 32,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12),
                Child = new TextBlock
                {
                    Text = Strings.Get("Running"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeManager.Brush("OnAccentBrush")
                }
            };
            Corners.SetSize(badge, CornerSize.Small);
            grid.Children.Add(badge);
        }
        return grid;
    }

    private static BitmapImage? LoadImage(string? path, int width)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return new BitmapImage(new Uri(path)) { DecodePixelWidth = width };
    }

    private void Select(int index, bool scroll = true)
    {
        if (index < 0 || index >= _games.Count || index == _selected) return;
        _selected = index;
        RefreshHero();
        if (scroll) _cards[index].StartBringIntoView(new BringIntoViewOptions { HorizontalAlignmentRatio = 0.3, AnimationDesired = !_prefs.ReduceMotion });
    }

    private void FocusSelection()
    {
        var state = _input.Mode == Core.Input.InputMode.Controller ? FocusState.Keyboard : FocusState.Programmatic;
        if (_selected >= 0 && _selected < _cards.Count)
        {
            _cards[_selected].Focus(state);
            _cards[_selected].StartBringIntoView(new BringIntoViewOptions { HorizontalAlignmentRatio = 0.3, AnimationDesired = false });
        }
        else _addCard?.Focus(state);
    }

    /// <summary>The hero, the backdrop and the ambient color for the selected game.</summary>
    private void RefreshHero(bool fetch = true)
    {
        var game = SelectedGame;
        var art = game is null ? null : _prefs.Art.GetValueOrDefault(game.Id);
        Home.HasSelection = game is not null;
        Home.HeroTitle = game is null ? Strings.Get("EmptyTitle") : LibraryOrder.DisplayTitle(game, art);
        var details = new List<string>();
        if (game is not null) details.Add(SourceLabel(game.Source));
        if (art?.IsMatched == true) details.AddRange(art.Details);
        Home.HeroDetails = string.Join("  ·  ", details);
        Home.HeroDescription = game is null ? Strings.Get("EmptyBody")
            : art?.IsMatched == true && art.Description.Length > 0 ? art.Description : "";
        var running = game is not null && _session?.Game.Id == game.Id;
        Home.IsRunning = _session is not null;
        Home.HeroStatus = running ? Strings.Get("RunningResume")
            : _session is not null ? Strings.Format("OtherRunning", LibraryOrder.DisplayTitle(_session.Game, _prefs.Art.GetValueOrDefault(_session.Game.Id))) : "";
        var background = art is { HasCatalogMedia: true } && File.Exists(art.Background) ? art.Background : null;
        ShowBackdrop(background);
        _ = UpdatePaletteAsync(background ?? (art is { HasCatalogMedia: true } ? art.Card : null));
        Motion.Entrance(Hero, _prefs.ReduceMotion);
        UpdateCards();
        if (fetch) ScheduleArtwork(game);
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
        UpdateCards();
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
        if (game is null || _activeDialog is not null) return;
        Sounds.Play(UiSound.Confirm);
        if (_session is not null)
        {
            if (_session.Game.Id != game.Id)
            {
                ShowToast(Strings.Format("CloseOtherFirst", LibraryOrder.DisplayTitle(_session.Game, _prefs.Art.GetValueOrDefault(_session.Game.Id))));
                Select(_games.FindIndex(entry => entry.Id == _session.Game.Id));
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
        // The game that just closed moves to the front of the row, like on a console.
        _games = LibraryOrder.Build(_prefs, _detected);
        _selected = _games.FindIndex(entry => entry.Id == game.Id);
        RenderCards();
        RefreshHero(fetch: false);
        if (timedOut) ShowToast(Strings.Get("WindowNotFound"));
        ShowHome();
    }

    private void CloseRunningGame()
    {
        if (_session is null) return;
        if (!_session.RequestClose()) ShowToast(Strings.Get("CloseFailed"));
    }

    // ───────── Covers and descriptions from the catalog ─────────

    private void ScheduleArtwork(Game? game)
    {
        if (game is null || _session is not null) return;
        var art = _prefs.Art.GetValueOrDefault(game.Id);
        if (art is { IsMatched: true, HasCatalogMedia: true } || _fetched.Contains(game.Id)) return;
        _artworkLookup?.Cancel();
        var lookup = _artworkLookup = new CancellationTokenSource();
        _ = AutoArtworkAsync(game, lookup.Token, delay: 600);
    }

    /// <summary>After a scan, looks up every game that has no cover yet, one at a time.</summary>
    private async Task PrefetchArtworkAsync()
    {
        foreach (var game in _games.ToList())
        {
            if (_exiting || _session is not null) return;
            var art = _prefs.Art.GetValueOrDefault(game.Id);
            if (art is { IsMatched: true, HasCatalogMedia: true } || _fetched.Contains(game.Id)) continue;
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
            CatalogMatch? match = null;
            if (art.IsMatched && Uri.TryCreate(art.CatalogUrl, UriKind.Absolute, out var page) && LaunchBoxParser.IsDetailsPage(page))
                match = new CatalogMatch(title, page);
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
        string? cover = null, background = null;
        try
        {
            cover = await TryDownload(entry.Cover, token);
            background = await TryDownload(entry.Background, token);
            token.ThrowIfCancellationRequested();
            ApplyArtwork(game, match, entry, cover, background);
        }
        finally
        {
            if (cover is not null) File.Delete(cover);
            if (background is not null) File.Delete(background);
        }
    }

    private async Task<string?> TryDownload(Uri? uri, CancellationToken token)
    {
        try { return await _catalog.DownloadAsync(uri, token); }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!token.IsCancellationRequested) { return null; }
    }

    private void ApplyArtwork(Game game, CatalogMatch match, CatalogEntry entry, string? cover, string? background)
    {
        var art = _prefs.ArtFor(game);
        art.Card = cover is not null ? Core.Settings.Preferences.StoreImage(_paths.ArtFolder, game.Id, "card", cover) : "";
        art.Background = background is not null ? Core.Settings.Preferences.StoreImage(_paths.ArtFolder, game.Id, "background", background) : "";
        art.CoverAspect = art.Card.Length > 0 ? entry.CoverAspect : 0;
        art.HasCatalogMedia = true;
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

    /// <summary>Grows (and lifts) an element a little while it has the pointer or the focus, with a soft spring.</summary>
    public static void AddLift(FrameworkElement element, double scale, double lift = 0)
    {
        bool pointer = false, focus = false;
        element.PointerEntered += (_, _) => { pointer = true; Update(); };
        element.PointerExited += (_, _) => { pointer = false; Update(); };
        element.PointerCanceled += (_, _) => { pointer = false; Update(); };
        element.GotFocus += (_, _) => { focus = true; Update(); };
        element.LostFocus += (_, _) => { focus = false; Update(); };

        void Update()
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
            var on = pointer || focus;
            var target = on ? (float)scale : 1f;
            if (!Enabled(Reduce))
            {
                visual.Scale = new Vector3(target, target, 1);
                return;
            }
            var compositor = visual.Compositor;
            var spring = compositor.CreateSpringVector3Animation();
            spring.FinalValue = new Vector3(target, target, 1);
            spring.DampingRatio = 0.62f;
            spring.Period = TimeSpan.FromMilliseconds(55);
            visual.StartAnimation("Scale", spring);
            if (lift > 0)
            {
                ElementCompositionPreview.SetIsTranslationEnabled(element, true);
                var move = compositor.CreateSpringVector3Animation();
                move.FinalValue = new Vector3(0, on ? -(float)lift : 0, 0);
                move.DampingRatio = 0.7f;
                move.Period = TimeSpan.FromMilliseconds(55);
                visual.StartAnimation("Translation", move);
            }
        }
    }

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

    /// <summary>The hero slides up a few pixels and fades in when the selection changes.</summary>
    public static void Entrance(UIElement element, bool reduce)
    {
        if (!Enabled(reduce)) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var compositor = visual.Compositor;
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, new Vector3(0, 14, 0));
        slide.InsertKeyFrame(1, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(new Vector2(.18f, .8f), new Vector2(.29f, 1)));
        slide.Duration = TimeSpan.FromMilliseconds(320);
        visual.StartAnimation("Translation", slide);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0.55f);
        fade.InsertKeyFrame(1, 1f);
        fade.Duration = TimeSpan.FromMilliseconds(320);
        visual.StartAnimation("Opacity", fade);
    }
}
