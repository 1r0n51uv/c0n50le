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
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace C0n50le;

public sealed partial class MainWindow
{
    private Control? _menuOrigin;
    private TaskCompletionSource<int>? _alert;
    private Control? _alertOrigin;
    private DispatcherQueueTimer? _toastTimer;

    private bool MenuOpen => MenuLayer.Visibility == Visibility.Visible;
    private bool AlertOpen => AlertLayer.Visibility == Visibility.Visible;
    private bool IsOverlayOpen => MenuOpen || AlertOpen || TourVisible;

    /// <summary>One entry of a context menu. A null label is a divider.</summary>
    private sealed record MenuEntry(string? Label, string Glyph = "", bool Checked = false, Func<Task>? Action = null)
    {
        public static MenuEntry Divider { get; } = new((string?)null);
    }

    // ───────── A card's options ─────────

    private void OpenCardMenu(Control? origin = null)
    {
        if (SelectedGame is not { } game || IsOverlayOpen) return;
        var running = _session?.Game.Id == game.Id;
        var entries = new List<MenuEntry>
        {
            new(running ? Strings.Get("Resume") : Strings.Get("Play"), running ? "" : "", Action: () => { StartOrResume(); return Task.CompletedTask; })
        };
        if (running) entries.Add(new(Strings.Get("CloseGame"), "", Action: () => { CloseRunningGame(); return Task.CompletedTask; }));
        entries.Add(MenuEntry.Divider);
        entries.Add(new(Strings.Get("EditGame"), "", Action: () => EditGameAsync(game)));
        entries.Add(new(Strings.Get("SearchArtwork"), "", Action: () => SearchArtworkAsync(game)));
        entries.Add(MenuEntry.Divider);
        entries.Add(new(Strings.Get("HideGame"), "", Action: () =>
        {
            if (_session?.Game.Id == game.Id) return Task.CompletedTask;
            _prefs.Hidden.Add(game.Id);
            Save();
            ShowToast(Strings.Format("GameHidden", LibraryOrder.DisplayTitle(game, _prefs.Art.GetValueOrDefault(game.Id))));
            return LoadLibraryAsync();
        }));
        if (game.IsManual)
            entries.Add(new(Strings.Get("RemoveGame"), "", Action: async () =>
            {
                if (_session?.Game.Id == game.Id) return;
                if (await AlertAsync(Strings.Get("RemoveGame"), Strings.Format("RemoveGameBody", game.Title), null,
                        Strings.Get("Remove"), Strings.Get("Cancel")) != 0) return;
                _prefs.Manual.RemoveAll(entry => entry.Id == game.Id);
                _prefs.Art.Remove(game.Id);
                _prefs.Played.Remove(game.Id);
                Save();
                await LoadLibraryAsync();
            }));
        var card = _selected >= 0 && _selected < _tiles.Count ? _tiles[_selected].Card : origin;
        OpenMenu(entries, card, card);
    }

    // ───────── Context menu ─────────

    /// <summary>A tvOS context menu beside <paramref name="anchor"/>, or in the middle without one.</summary>
    private void OpenMenu(IReadOnlyList<MenuEntry> entries, FrameworkElement? anchor, Control? origin)
    {
        if (AlertOpen) return;
        _menuOrigin = origin;
        MenuItems.Children.Clear();
        Button? first = null, chosen = null;
        var indent = entries.Any(entry => entry.Glyph.Length > 0 || entry.Checked);
        foreach (var entry in entries)
        {
            if (entry.Label is null)
            {
                MenuItems.Children.Add(new Border { Height = 10, Margin = new Thickness(-10, 4, -10, 4), Background = ThemeManager.Brush("SeparatorBrush"), Opacity = 0.35 });
                continue;
            }
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(indent ? 70 : 24) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var glyph = entry.Checked ? "" : entry.Glyph;
            if (glyph.Length > 0)
                row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var label = new TextBlock { Text = entry.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var button = new Button { Style = ThemeManager.Style("TvMenuItem"), Content = row };
            AutomationProperties.SetName(button, entry.Label);
            if (entry.Checked) AutomationProperties.SetItemStatus(button, Strings.Get("Selected"));
            button.Click += async (_, _) =>
            {
                Sounds.Play(UiSound.Confirm);
                CloseMenu(restoreFocus: true);
                if (entry.Action is not null) await RunAsync(entry.Action);
            };
            MenuItems.Children.Add(button);
            first ??= button;
            if (entry.Checked) chosen = button;
        }
        PlaceMenu(anchor);
        MenuLayer.Visibility = Visibility.Visible;
        Sounds.Play(UiSound.Open);
        Motion.Fade(MenuPanel, 1, 160, _prefs.ReduceMotion);
        var target = chosen ?? first;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => target?.Focus(FocusState.Keyboard));
    }

    /// <summary>Beside the anchor (to its right, or left when there is no room), never off screen.</summary>
    private void PlaceMenu(FrameworkElement? anchor)
    {
        MenuPanel.Measure(new Windows.Foundation.Size(MenuPanel.Width, double.PositiveInfinity));
        var height = Math.Min(MenuPanel.DesiredSize.Height, Stage.ActualHeight - 80);
        if (anchor is null || anchor.ActualWidth <= 0)
        {
            MenuPanel.Margin = new Thickness((Stage.ActualWidth - MenuPanel.Width) / 2, (Stage.ActualHeight - height) / 2, 0, 0);
            return;
        }
        var bounds = anchor.TransformToVisual(Stage).TransformBounds(new Windows.Foundation.Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
        if (bounds.Width > MenuPanel.Width * 1.3)
        {
            // A settings row: the choices open at its right end, by the value, under the row (above it near the bottom).
            var x = Math.Clamp(bounds.Right - MenuPanel.Width, 40, Stage.ActualWidth - MenuPanel.Width - 40);
            var y = bounds.Bottom + 8 + height <= Stage.ActualHeight - 40 ? bounds.Bottom + 8 : Math.Max(40, bounds.Y - height - 8);
            MenuPanel.Margin = new Thickness(x, y, 0, 0);
            return;
        }
        var gap = 48;
        var left = bounds.Right + gap;
        if (left + MenuPanel.Width > Stage.ActualWidth - 40) left = bounds.X - gap - MenuPanel.Width;
        if (left < 40) left = Math.Clamp(bounds.X, 40, Stage.ActualWidth - MenuPanel.Width - 40);
        var top = Math.Clamp(bounds.Y + bounds.Height / 2 - height / 2, 40, Math.Max(40, Stage.ActualHeight - height - 40));
        MenuPanel.Margin = new Thickness(left, top, 0, 0);
    }

    private void OnMenuBackgroundTapped(object sender, TappedRoutedEventArgs e) => CloseMenu();

    private void OnMenuPanelTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void CloseMenu(bool restoreFocus = true)
    {
        if (!MenuOpen) return;
        MenuLayer.Visibility = Visibility.Collapsed;
        MenuItems.Children.Clear();
        if (!restoreFocus) return;
        Sounds.Play(UiSound.Back);
        var origin = _menuOrigin;
        _menuOrigin = null;
        if (origin is not null && origin.Visibility == Visibility.Visible && IsInside(origin, ActiveLayer)) origin.Focus(FocusState.Keyboard);
        else FocusFirst(ActiveLayer);
    }

    // ───────── Alert ─────────

    /// <summary>
    /// A tvOS alert: title, description, optional content (fields, results) and stacked buttons. Returns the index of the
    /// button chosen, or -1 when dismissed with Back / Esc.
    /// </summary>
    private async Task<int> AlertAsync(string title, string body, FrameworkElement? content, params string[] buttons)
    {
        if (_alert is not null) return -1;
        CloseMenu(restoreFocus: false);
        _alertOrigin = Root.XamlRoot is null ? null : FocusManager.GetFocusedElement(Root.XamlRoot) as Control;
        AlertTitle.Text = title;
        AlertBody.Text = body;
        AlertBody.Visibility = body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AlertContent.Content = content;
        AlertPanel.Width = content is null ? 560 : 760;
        AlertButtons.Children.Clear();
        var completion = _alert = new TaskCompletionSource<int>();
        for (var index = 0; index < buttons.Length; index++)
        {
            var choice = index;
            var button = new Button { Style = ThemeManager.Style("TvAlertButton"), Content = buttons[index] };
            button.Click += (_, _) =>
            {
                Sounds.Play(UiSound.Confirm);
                completion.TrySetResult(choice);
            };
            AlertButtons.Children.Add(button);
        }
        AlertLayer.Visibility = Visibility.Visible;
        Sounds.Play(UiSound.Open);
        Motion.Fade(AlertPanel, 1, 180, _prefs.ReduceMotion);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            var first = content is null ? null : FocusManager.FindFirstFocusableElement(AlertContent) as Control;
            (first ?? AlertButtons.Children.OfType<Button>().FirstOrDefault())?.Focus(FocusState.Keyboard);
        });
        try { return await completion.Task; }
        finally
        {
            _alert = null;
            AlertLayer.Visibility = Visibility.Collapsed;
            AlertContent.Content = null;
            AlertButtons.Children.Clear();
            var origin = _alertOrigin;
            _alertOrigin = null;
            if (origin is not null && origin.Visibility == Visibility.Visible && IsInside(origin, ActiveLayer)) origin.Focus(FocusState.Keyboard);
            else FocusFirst(ActiveLayer);
        }
    }

    private void DismissAlert()
    {
        if (_alert is null) return;
        Sounds.Play(UiSound.Back);
        _alert.TrySetResult(-1);
    }

    /// <summary>Enables or disables an alert's button (e.g. Apply until a result is picked).</summary>
    private void SetAlertButtonEnabled(int index, bool enabled)
    {
        if (index < AlertButtons.Children.Count && AlertButtons.Children[index] is Button button) button.IsEnabled = enabled;
    }

    // ───────── Add, edit, search ─────────

    private async Task AddGameAsync()
    {
        if (AlertOpen) return;
        await RunAsync(async () =>
        {
            var file = await PickFileAsync(".exe");
            if (file is null) return;
            var game = new Game { Id = Guid.NewGuid().ToString("N"), Path = file, Source = GameSource.Manual };
            var fields = new StackPanel { Spacing = 14 };
            var name = Field(fields, Strings.Get("FieldName"), game.Title);
            var arguments = Field(fields, Strings.Get("FieldArgumentsOptional"), "");
            if (await AlertAsync(Strings.Get("AddGame"), "", fields, Strings.Get("Add"), Strings.Get("Cancel")) != 0) return;
            game.Name = name.Text.Trim();
            game.Arguments = arguments.Text.Trim();
            _prefs.Manual.Add(game);
            Save();
            await LoadLibraryAsync(select: game.Id);
        });
    }

    private async Task EditGameAsync(Game game)
    {
        var fields = new StackPanel { Spacing = 14 };
        var name = Field(fields, Strings.Get("FieldName"), game.Title);
        var path = Field(fields, Strings.Get("FieldExecutable"), game.Path);
        var arguments = Field(fields, Strings.Get("FieldArguments"), game.Arguments);
        name.IsEnabled = path.IsEnabled = arguments.IsEnabled = game.IsManual;
        var watch = Field(fields, Strings.Get("FieldWatch"), _prefs.Watched.GetValueOrDefault(game.Id, game.WatchProcess));
        var body = game.IsManual ? "" : Strings.Format("FoundGameNote", SourceLabel(game.Source));
        if (await AlertAsync(Strings.Get("EditGame"), body, fields, Strings.Get("Save"), Strings.Get("Cancel")) != 0) return;
        if (game.IsManual)
        {
            var executable = path.Text.Trim().Trim('"');
            if (!File.Exists(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                ShowToast(Strings.Get("PickExistingExe"));
                return;
            }
            game.Name = name.Text.Trim();
            game.Path = executable;
            game.Arguments = arguments.Text.Trim();
        }
        if (watch.Text.Trim().Length > 0) _prefs.Watched[game.Id] = watch.Text.Trim();
        else _prefs.Watched.Remove(game.Id);
        Save();
        await LoadLibraryAsync(select: game.Id);
    }

    /// <summary>Search the catalog by another title, preview logo and background, and apply on confirm.</summary>
    private async Task SearchArtworkAsync(Game game)
    {
        _artworkLookup?.Cancel();
        var panel = new StackPanel { Spacing = 16 };
        var queryRow = new Grid { ColumnSpacing = 14 };
        queryRow.ColumnDefinitions.Add(new ColumnDefinition());
        queryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var query = new TextBox
        {
            Style = ThemeManager.Style("TvTextField"), Text = LibraryOrder.SearchTitle(game, _prefs.Art.GetValueOrDefault(game.Id)),
            PlaceholderText = Strings.Get("FieldSearchTitle")
        };
        queryRow.Children.Add(query);
        var search = new Button { Style = ThemeManager.Style("TvAlertButton"), Content = Strings.Get("Search"), Width = 180 };
        Grid.SetColumn(search, 1);
        queryRow.Children.Add(search);
        panel.Children.Add(queryRow);
        var message = new TextBlock { FontSize = 23, TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Brush("TextSecondaryBrush") };
        panel.Children.Add(message);
        var results = new StackPanel { Spacing = 4 };
        panel.Children.Add(new ScrollViewer { Content = results, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, IsTabStop = false });
        var preview = new Grid { Height = 180, CornerRadius = new CornerRadius(30), Visibility = Visibility.Collapsed, Background = ThemeManager.Brush("ControlBrush") };
        var previewBackground = new Image { Stretch = Stretch.UniformToFill };
        var previewLogo = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(40, 24, 40, 24) };
        var previewLabel = new TextBlock
        {
            FontSize = 23, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(20, 0, 20, 14),
            VerticalAlignment = VerticalAlignment.Bottom, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        };
        preview.Children.Add(previewBackground);
        preview.Children.Add(previewLogo);
        preview.Children.Add(previewLabel);
        panel.Children.Add(preview);

        using var cancellation = new CancellationTokenSource();
        CancellationTokenSource? previewRequest = null;
        (CatalogMatch Match, CatalogEntry Entry, CatalogFiles Files)? chosen = null;

        void Discard()
        {
            chosen?.Files.Dispose();
            chosen = null;
        }

        async Task Pick(CatalogMatch match)
        {
            previewRequest?.Cancel();
            var request = previewRequest = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            SetAlertButtonEnabled(0, false);
            Discard();
            preview.Visibility = Visibility.Visible;
            previewBackground.Source = previewLogo.Source = null;
            previewLabel.Text = Strings.Get("LoadingPreview");
            try
            {
                var entry = await _catalog.GetAsync(match, Strings.Language, request.Token);
                var files = await DownloadAllAsync(entry, request.Token);
                if (request.IsCancellationRequested) { files.Dispose(); return; }
                chosen = (match, entry, files);
                previewBackground.Source = LoadImage(files.Background ?? files.Banner ?? files.Cover, 900);
                previewLogo.Source = LoadImage(files.Logo, 600);
                previewLabel.Text = files.Logo is null ? match.Label : match.Platform;
                SetAlertButtonEnabled(0, true);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { previewLabel.Text = Strings.Format("PreviewFailed", ex.Message); }
        }

        async Task Search()
        {
            search.IsEnabled = false;
            results.Children.Clear();
            message.Text = Strings.Get("Searching");
            try
            {
                var found = await _catalog.SearchAsync(query.Text.Trim(), cancellation.Token);
                message.Text = found.Count > 0 ? Strings.Get("PickResult") : Strings.Get("NoResults");
                foreach (var match in found)
                {
                    var item = new Button { Style = ThemeManager.Style("TvListItem"), MinHeight = 60 };
                    var line = new Grid();
                    line.ColumnDefinitions.Add(new ColumnDefinition());
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    line.Children.Add(new TextBlock { Text = match.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                    var platform = new TextBlock { Text = match.Platform, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
                    Grid.SetColumn(platform, 1);
                    line.Children.Add(platform);
                    item.Content = line;
                    item.Click += async (_, _) => await Pick(match);
                    results.Children.Add(item);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { message.Text = Strings.Format("SearchFailed", ex.Message); }
            finally { search.IsEnabled = true; }
        }

        search.Click += async (_, _) => await Search();
        query.KeyDown += async (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.Enter) return;
            args.Handled = true;
            await Search();
        };
        var alert = AlertAsync(Strings.Get("SearchArtwork"), "", panel, Strings.Get("Apply"), Strings.Get("Cancel"));
        SetAlertButtonEnabled(0, false);
        _ = Search();
        var answer = await alert;
        cancellation.Cancel();
        previewBackground.Source = previewLogo.Source = null;
        if (answer == 0 && chosen is { } pick)
        {
            try { ApplyArtwork(game, pick.Match, pick.Entry, pick.Files); }
            finally { Discard(); }
        }
        else Discard();
    }

    private async Task<string?> PickFileAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker();
        foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, _handle);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _handle);
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    /// <summary>A labelled tvOS text field.</summary>
    private static TextBox Field(Panel panel, string title, string value)
    {
        var label = new TextBlock { Text = title, FontSize = 23, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = ThemeManager.Brush("TextSecondaryBrush"), Margin = new Thickness(6, 4, 0, -6) };
        panel.Children.Add(label);
        var field = new TextBox { Text = value, Style = ThemeManager.Style("TvTextField") };
        AutomationProperties.SetName(field, title);
        panel.Children.Add(field);
        return field;
    }

    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            AppLog.Write($"Azione: {ex.Message}");
            ShowToast(ex.Message);
        }
    }

    private void CloseOverlays()
    {
        DismissAlert();
        CloseMenu(restoreFocus: false);
    }

    // ───────── Notices ─────────

    private void ShowToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ToastText.Text = message;
        Motion.Fade(Toast, 1, 200, _prefs.ReduceMotion);
        _toastTimer ??= DispatcherQueue.CreateTimer();
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(3 + message.Length / 20.0, 4, 9));
        _toastTimer.IsRepeating = false;
        _toastTimer.Tick -= HideToast;
        _toastTimer.Tick += HideToast;
        _toastTimer.Start();
    }

    private void HideToast(DispatcherQueueTimer sender, object args) => Motion.Fade(Toast, 0, 400, _prefs.ReduceMotion);
}
