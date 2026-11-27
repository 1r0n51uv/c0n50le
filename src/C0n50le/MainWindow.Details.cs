using System.Numerics;
using C0n50le.Core.Audio;
using C0n50le.Core.Catalog;
using C0n50le.Core.Library;
using C0n50le.Core.Localization;
using C0n50le.Services;
using C0n50le.Ui;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace C0n50le;

/// <summary>
/// A game's detail page. Everything starts from a valid title in the catalog: once it is assigned, the card picture,
/// the backgrounds (one or more, from LaunchBox, Steam or the PC, shown in turn) and the description follow from it.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string[] PictureExtensions = [".jpg", ".jpeg", ".png", ".webp", ".bmp"];
    private Game? _detailsGame;
    private double _nextAlertWidth;

    private bool DetailsOpen => DetailsPage.Visibility == Visibility.Visible;
    private bool PageOpen => SystemPage.Visibility == Visibility.Visible || DetailsOpen;

    private void OpenDetails(Game game)
    {
        if (PageOpen || IsOverlayOpen) return;
        _detailsGame = game;
        Sounds.Play(UiSound.Open);
        DetailsPage.Visibility = Visibility.Visible;
        HomePage.Visibility = Visibility.Collapsed;
        TopBar.Visibility = HomeHints.Visibility = Visibility.Collapsed;
        PageDim.Opacity = 1;
        Motion.Fade(PageDim, 1, 220, _prefs.ReduceMotion);
        RenderDetails(0);
    }

    private void CloseDetails()
    {
        if (!DetailsOpen) return;
        Sounds.Play(UiSound.Back);
        DetailsPage.Visibility = Visibility.Collapsed;
        HomePage.Visibility = Visibility.Visible;
        TopBar.Visibility = Visibility.Visible;
        HomeHints.Visibility = Home.IsControllerMode ? Visibility.Visible : Visibility.Collapsed;
        Motion.Fade(PageDim, 0, 220, _prefs.ReduceMotion);
        var id = _detailsGame?.Id;
        _detailsGame = null;
        DetailsRows.Children.Clear();
        RenderCards();
        RefreshHero(fetch: false);
        if (id is not null && _games.FindIndex(game => game.Id == id) is >= 0 and var index) _selected = index;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FocusSelection);
    }

    private void OnDetailsBackClick(object sender, RoutedEventArgs e) => CloseDetails();

    private void FocusDetails() => DetailsRows.Children.OfType<Button>().FirstOrDefault()?.Focus(FocusState.Keyboard);

    /// <summary>The card as it looks in the dock, the backgrounds in turn, and the rows.</summary>
    private void RenderDetails(int? focusRow = null)
    {
        if (_detailsGame is not { } game) return;
        var art = _prefs.ArtFor(game);
        var title = LibraryOrder.DisplayTitle(game, art);
        DetailsHeading.Text = title;
        DetailsSubtitle.Text = Strings.Get("GameDetails");
        DetailsCard.Children.Clear();
        DetailsCard.Children.Add(CardContent(title, art, _session?.Game.Id == game.Id));
        // Beside the card: the rating square, then the facts wrapping under it.
        DetailsInfo.Children.Clear();
        foreach (var fact in GameBadges.For(game, art, SourceLabel(game.Source), 24, 520))
        {
            fact.HorizontalAlignment = HorizontalAlignment.Left;
            if (fact is TextBlock text) text.MaxLines = 4;
            DetailsInfo.Children.Add(fact);
        }
        DetailsBackgrounds.Children.Clear();
        foreach (var path in art.BackgroundsToShow(File.Exists).Take(5))
            DetailsBackgrounds.Children.Add(new Grid
            {
                Width = 88, Height = 50, CornerRadius = new CornerRadius(10), Background = ThemeManager.Brush("ControlBrush"),
                Children = { new Image { Source = LoadImage(path, 180), Stretch = Stretch.UniformToFill } }
            });

        DetailsRows.Children.Clear();
        DetailsFooter.Text = "";
        var matched = art.IsMatched;
        DetailRow("DetailsTitleRow", "DetailsTitleRowBody", matched ? art.CatalogTitle : Strings.Get("TitleNotAssigned"), true, async () =>
        {
            var before = art.CatalogId;
            await SearchArtworkAsync(game);
            var after = _prefs.ArtFor(game);
            if (after.IsMatched && after.CatalogId != before)
            {
                ShowToast(Strings.Format("TitleAssigned", after.CatalogTitle));
                _ = RefreshDescriptionNowAsync(game, quiet: true);
            }
            RenderDetails(0);
        });
        DetailRow("CardImage", "CardImageBody",
            File.Exists(art.CardImage) ? Strings.Get("CardImageChosen") : Strings.Get("CardImageAuto"), matched,
            () => PickArtAsync(game, ArtUse.Card));
        DetailRow("LogoRow", "LogoRowBody",
            File.Exists(art.LogoImage) ? Strings.Get("CardImageChosen") : art.LogoToShow(File.Exists) is not null ? Strings.Get("CardImageAuto")
            : Strings.Get("NotSet"), matched, () => PickArtAsync(game, ArtUse.Logo));
        var backgrounds = art.Backgrounds.Count(File.Exists);
        DetailRow("Backgrounds", "BackgroundsBody",
            backgrounds > 0 ? Strings.Format("BackgroundsCount", backgrounds)
            : art.BackgroundsToShow(File.Exists).Count > 0 ? Strings.Get("BackgroundsCatalog") : Strings.Get("NotSet"), matched,
            () => PickArtAsync(game, ArtUse.Background));
        var description = art.Descriptions.GetValueOrDefault(Strings.Language + "|" + DescriptionTitle(game, art));
        var wikipedia = art.DescriptionSource == DescriptionSources.Wikipedia;
        DetailRow("DescriptionRow", "DescriptionRowBody",
            description is { Text.Length: > 0 } ? description.SourceName.Split(' ')[0] : Strings.Get("DescriptionNone"), matched, () =>
            {
                // Which text to use for this game, or fetch it again.
                void Use(string source)
                {
                    art.DescriptionSource = source;
                    art.Descriptions.Remove(Strings.Language + "|" + DescriptionTitle(game, art));
                    Save();
                    _descriptionRequests.Clear();
                    _ = RefreshDescriptionNowAsync(game, quiet: false);
                }
                OpenMenu(
                [
                    new(Strings.Get("DescriptionLaunchBox"), Checked: !wikipedia, Action: () => { Use(DescriptionSources.Automatic); return Task.CompletedTask; }),
                    new(Strings.Get("DescriptionWikipedia"), Checked: wikipedia, Action: () => { Use(DescriptionSources.Wikipedia); return Task.CompletedTask; }),
                    MenuEntry.Divider,
                    new(Strings.Get("DescriptionRefresh"), "\uE72C", Action: () => RefreshDescriptionNowAsync(game, quiet: false))
                ], FocusedButton(), FocusedButton());
                return Task.CompletedTask;
            });
        DetailRow("EditGame", "EditGameBody", "", true, async () =>
        {
            await EditGameAsync(game);
            RenderDetails(5);
        });
        if (focusRow is { } row && row >= 0 && row < DetailsRows.Children.Count && DetailsRows.Children[row] is Button button)
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => button.Focus(FocusState.Keyboard));
    }

    /// <summary>A detail row. Until a title is assigned, the rows that depend on it are dimmed and say so.</summary>
    private void DetailRow(string titleKey, string bodyKey, string value, bool enabled, Func<Task> action)
    {
        var button = Row(titleKey, enabled ? bodyKey : "DetailsNeedsTitle", value, chevron: true, DetailsRows, DetailsFooter);
        if (!enabled) button.Opacity = 0.45;
        button.Click += async (_, _) =>
        {
            if (!enabled)
            {
                Sounds.Play(UiSound.Back);
                ShowToast(Strings.Get("DetailsNeedsTitle"));
                return;
            }
            Sounds.Play(UiSound.Confirm);
            await RunAsync(action);
        };
    }

    private async Task RefreshDescriptionNowAsync(Game game, bool quiet)
    {
        var art = _prefs.ArtFor(game);
        var title = DescriptionTitle(game, art);
        var language = Strings.Language;
        var key = language + "|" + title;
        var result = await FindDescriptionAsync(game, title, language, _descriptionLifetime.Token);
        if (result.Text.Length == 0)
        {
            if (!quiet) ShowToast(Strings.Get("DescriptionNotFound"));
            return;
        }
        art.Descriptions[key] = result;
        _descriptionRequests.Add(game.Id + "|" + key);
        Save();
        if (!quiet) ShowToast(Strings.Get("DescriptionUpdated"));
        if (SelectedGame?.Id == game.Id && !_heroShowsAction) RefreshDescription(game);
        if (_detailsGame?.Id == game.Id) RenderDetails(4);
    }

    // ───────── Choosing pictures ─────────

    /// <summary>One picture in the chooser: a file already on disk, or a picture online to download on save.</summary>
    private sealed class ArtChoice
    {
        public string? File { get; init; }
        public ArtCandidate? Online { get; init; }
        public required string Label { get; init; }
        public bool Selected { get; set; }
        public Button? Tile { get; set; }
    }

    /// <summary>
    /// Pictures from every source in one grid: the current ones, LaunchBox and Steam for the assigned title, and a file
    /// from the PC. The card takes one; the backgrounds take as many as wanted.
    /// </summary>
    private async Task PickArtAsync(Game game, ArtUse use)
    {
        var art = _prefs.ArtFor(game);
        var multi = use == ArtUse.Background;
        var logo = use == ArtUse.Logo;
        var choices = new List<ArtChoice>();
        var panel = new StackPanel { Spacing = 14 };
        var status = new TextBlock
        {
            FontSize = 23, TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Brush("TextSecondaryBrush"), Text = Strings.Get("LoadingImages")
        };
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 272, ItemHeight = 168, MaximumRowsOrColumns = 3 };
        panel.Children.Add(status);
        panel.Children.Add(new ScrollViewer
        {
            Content = grid, MaxHeight = 540, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, IsTabStop = false,
            HorizontalScrollMode = ScrollMode.Disabled
        });

        void Choose(ArtChoice choice)
        {
            Sounds.Play(UiSound.Move);
            if (!multi)
                foreach (var other in choices.Where(other => other != choice && other.Selected))
                {
                    other.Selected = false;
                    MarkTile(other);
                }
            choice.Selected = multi ? !choice.Selected : true;
            MarkTile(choice);
        }

        void Add(ArtChoice choice, int? at = null)
        {
            choices.Add(choice);
            choice.Tile = ArtTile(choice, () => Choose(choice));
            if (at is { } index) grid.Children.Insert(Math.Min(index, grid.Children.Count), choice.Tile);
            else grid.Children.Add(choice.Tile);
            MarkTile(choice);
        }

        // From the PC: always first, so a picture can be chosen even with nothing online.
        var fromPc = PcTile();
        fromPc.Click += async (_, _) =>
        {
            Sounds.Play(UiSound.Confirm);
            if (await PickFileAsync(PictureExtensions) is not { } file) return;
            var choice = new ArtChoice { File = file, Label = Path.GetFileName(file) };
            Add(choice, 1);
            Choose(choice);
            choice.Tile?.Focus(FocusState.Keyboard);
        };
        grid.Children.Add(fromPc);

        if (use == ArtUse.Card && File.Exists(art.CardImage))
            Add(new ArtChoice { File = art.CardImage, Label = Strings.Get("CurrentImage"), Selected = true });
        if (logo && art.LogoToShow(File.Exists) is { } currentLogo)
            Add(new ArtChoice { File = currentLogo, Label = Strings.Get("CurrentImage"), Selected = true });
        if (use == ArtUse.Background)
        {
            var current = art.Backgrounds.Where(File.Exists).ToList();
            if (current.Count == 0 && art.BackgroundsToShow(File.Exists) is [var catalog]) current.Add(catalog);
            foreach (var file in current) Add(new ArtChoice { File = file, Label = Strings.Get("CurrentImage"), Selected = true });
        }

        using var cancellation = new CancellationTokenSource();
        _ = LoadOnlineAsync();

        async Task LoadOnlineAsync()
        {
            var found = new List<ArtCandidate>();
            async Task Collect(Func<Task<List<ArtCandidate>>> source)
            {
                try { found.AddRange(await source()); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                {
                    AppLog.Write($"Immagini: {ex.Message}");
                }
            }
            var gallery = Uri.TryCreate(art.CatalogUrl, UriKind.Absolute, out var page)
                ? Collect(() => _catalog.GalleryAsync(page, cancellation.Token)) : Task.CompletedTask;
            var steam = Collect(() => _catalog.SteamArtAsync(game.SteamId, DescriptionTitle(game, art), cancellation.Token));
            await Task.WhenAll(gallery, steam);
            if (cancellation.IsCancellationRequested) return;
            foreach (var candidate in ArtSources.For(use, found).Take(30))
                Add(new ArtChoice { Online = candidate, Label = $"{candidate.Source} · {candidate.Label}" });
            status.Text = choices.Any(choice => choice.Online is not null) ? Strings.Get(multi ? "PickBackgroundsBody" : logo ? "PickLogoBody" : "PickCardImageBody")
                : Strings.Get("NoImages");
        }

        _nextAlertWidth = 900;
        var buttons = !multi
            ? new[] { Strings.Get("Save"), Strings.Get("UseAutomatic"), Strings.Get("Cancel") }
            : new[] { Strings.Get("Save"), Strings.Get("Cancel") };
        var answer = await AlertAsync(Strings.Get(multi ? "PickBackgrounds" : logo ? "PickLogo" : "PickCardImage"), "", panel, buttons);
        cancellation.Cancel();
        if (!multi && answer == 1)
        {
            if (logo) art.LogoImage = "";
            else art.CardImage = "";
            Save();
            UpdateCards();
            if (!_heroShowsAction) RefreshHero(fetch: false);
            RenderDetails(logo ? 2 : 1);
            return;
        }
        if (answer != 0) return;

        ShowToast(Strings.Get("SavingImages"));
        var kind = use switch { ArtUse.Card => "cardimage", ArtUse.Logo => "logoimage", _ => "backdrop" };
        var saved = new List<string>();
        try
        {
            foreach (var choice in choices.Where(choice => choice.Selected))
            {
                if (choice.File is { } file)
                {
                    // Pictures already in the art folder are kept as they are; others are copied in.
                    saved.Add(Path.GetDirectoryName(file) is { } folder && Path.GetFullPath(folder) == Path.GetFullPath(_paths.ArtFolder)
                        ? file : Core.Settings.Preferences.StoreImage(_paths.ArtFolder, game.Id, kind, file));
                    continue;
                }
                var temporary = await _catalog.DownloadAsync(choice.Online!.Url, CancellationToken.None);
                if (temporary is null) continue;
                try { saved.Add(Core.Settings.Preferences.StoreImage(_paths.ArtFolder, game.Id, kind, temporary)); }
                finally { File.Delete(temporary); }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            ShowToast(Strings.Format("ImagesFailed", ex.Message));
            return;
        }
        if (use == ArtUse.Card) art.CardImage = saved.FirstOrDefault() ?? art.CardImage;
        else if (logo) art.LogoImage = saved.FirstOrDefault() ?? art.LogoImage;
        else art.Backgrounds = saved;
        Save();
        ShowToast(Strings.Get("ImagesSaved"));
        UpdateCards();
        if (!_heroShowsAction) RefreshHero(fetch: false);
        RenderDetails(use switch { ArtUse.Card => 1, ArtUse.Logo => 2, _ => 3 });
    }

    /// <summary>A thumbnail: grows a little when focused, shows a tick and a ring when chosen.</summary>
    private Button ArtTile(ArtChoice choice, Action toggle)
    {
        // Logos are transparent titles: whole, centred, on the dark tile.
        var whole = choice.Label.Contains("Logo", StringComparison.OrdinalIgnoreCase) || (choice.File?.Contains("-logo", StringComparison.OrdinalIgnoreCase) ?? false);
        var picture = new Image { Stretch = whole ? Stretch.Uniform : Stretch.UniformToFill, Margin = whole ? new Thickness(22, 16, 22, 40) : new Thickness(0) };
        var bitmap = new BitmapImage { DecodePixelWidth = 360 };
        if (choice.File is { } file && File.Exists(file)) bitmap.UriSource = new Uri(file);
        else if (choice.Online is { } online) bitmap.UriSource = online.Url;
        picture.Source = bitmap;
        var content = new Grid { CornerRadius = new CornerRadius(22), Background = ThemeManager.Brush("ControlBrush") };
        content.Children.Add(picture);
        content.Children.Add(new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom, Height = 56,
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1),
                GradientStops = { new GradientStop { Color = Windows.UI.Color.FromArgb(0, 0, 0, 0), Offset = 0 },
                                  new GradientStop { Color = Windows.UI.Color.FromArgb(170, 0, 0, 0), Offset = 1 } }
            }
        });
        content.Children.Add(new TextBlock
        {
            Text = choice.Label, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 0, 14, 10),
            VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        });
        var ring = new Border
        {
            Tag = "ring", CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(5), IsHitTestVisible = false,
            BorderBrush = ThemeManager.Brush("FocusBrush"), Visibility = Visibility.Collapsed
        };
        content.Children.Add(ring);
        var tick = new Border
        {
            Tag = "tick", Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 10, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed,
            Background = ThemeManager.Brush("FocusBrush"),
            Child = new FontIcon { Glyph = "", FontSize = 18, Foreground = ThemeManager.Brush("FocusTextBrush") }
        };
        content.Children.Add(tick);
        var tile = new Button
        {
            Style = ThemeManager.Style("TvCard"), Width = 250, Height = 146, Content = content, CornerRadius = new CornerRadius(22),
            Margin = new Thickness(0, 0, 22, 22)
        };
        AutomationProperties.SetName(tile, choice.Label);
        bitmap.ImageFailed += (_, _) =>
        {
            // A picture that isn't there (some Steam apps lack a hero) leaves the grid instead of an empty tile.
            if (choice.Online is not null)
            {
                choice.Selected = false;
                tile.Visibility = Visibility.Collapsed;
            }
        };
        tile.Click += (_, _) => toggle();
        GrowOnFocus(tile);
        return tile;
    }

    private Button PcTile()
    {
        var content = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new FontIcon { Glyph = "", FontSize = 34 });
        content.Children.Add(new TextBlock { Text = Strings.Get("FromPc"), FontSize = 19, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        var tile = new Button
        {
            Style = ThemeManager.Style("TvCard"), Width = 250, Height = 146, CornerRadius = new CornerRadius(22),
            Background = ThemeManager.Brush("ControlBrush"), Content = content, Margin = new Thickness(0, 0, 22, 22)
        };
        AutomationProperties.SetName(tile, Strings.Get("FromPc"));
        GrowOnFocus(tile);
        return tile;
    }

    private static void GrowOnFocus(Button tile)
    {
        void Grow(float scale)
        {
            tile.ScaleTransition = Motion.Enabled(Motion.Reduce) ? new Vector3Transition { Duration = TimeSpan.FromMilliseconds(160) } : null;
            tile.CenterPoint = new Vector3((float)tile.Width / 2, (float)tile.Height / 2, 0);
            tile.Scale = new Vector3(scale, scale, 1);
        }
        tile.GotFocus += (_, _) => Grow(1.06f);
        tile.LostFocus += (_, _) => Grow(1f);
    }

    private static void MarkTile(ArtChoice choice)
    {
        if (choice.Tile?.Content is not Grid content) return;
        foreach (var child in content.Children.OfType<Border>())
            if (child.Tag is "ring" or "tick") child.Visibility = choice.Selected ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetItemStatus(choice.Tile, choice.Selected ? Strings.Get("Selected") : "");
    }
}
