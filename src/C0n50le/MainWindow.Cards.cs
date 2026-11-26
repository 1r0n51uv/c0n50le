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
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace C0n50le;

public sealed partial class MainWindow
{
    private ContentDialog? _activeDialog;
    private Control? _pickerOrigin;
    private DispatcherQueueTimer? _toastTimer;

    private bool IsOverlayOpen => _activeDialog is not null || Picker.Visibility == Visibility.Visible || TourVisible;

    /// <summary>One entry of a picker.</summary>
    private sealed record PickerOption(string Label, string Glyph = "", bool Selected = false, Func<Task>? Action = null, bool Danger = false);

    // ───────── Card options ─────────

    private void OpenCardMenu(Control? origin = null)
    {
        if (SelectedGame is not { } game || IsOverlayOpen) return;
        var running = _session?.Game.Id == game.Id;
        var options = new List<PickerOption>
        {
            new(running ? Strings.Get("Resume") : Strings.Get("Play"), running ? "" : "", Action: () => { StartOrResume(); return Task.CompletedTask; })
        };
        if (running) options.Add(new(Strings.Get("CloseGame"), "", Action: () => { CloseRunningGame(); return Task.CompletedTask; }));
        options.Add(new(Strings.Get("EditGame"), "", Action: () => EditGameAsync(game)));
        options.Add(new(Strings.Get("SearchArtwork"), "", Action: () => SearchArtworkAsync(game)));
        options.Add(new(Strings.Get("HideGame"), "", Action: () =>
        {
            if (_session?.Game.Id == game.Id) return Task.CompletedTask;
            _prefs.Hidden.Add(game.Id);
            Save();
            ShowToast(Strings.Format("GameHidden", LibraryOrder.DisplayTitle(game, _prefs.Art.GetValueOrDefault(game.Id))));
            return LoadLibraryAsync();
        }));
        if (game.IsManual)
            options.Add(new(Strings.Get("RemoveGame"), "", Danger: true, Action: async () =>
            {
                if (_session?.Game.Id == game.Id) return;
                if (!await ConfirmAsync(Strings.Get("RemoveGame"), Strings.Format("RemoveGameBody", game.Title), Strings.Get("Remove"))) return;
                _prefs.Manual.RemoveAll(entry => entry.Id == game.Id);
                _prefs.Art.Remove(game.Id);
                _prefs.Played.Remove(game.Id);
                Save();
                await LoadLibraryAsync();
            }));
        var card = _selected >= 0 && _selected < _cards.Count ? _cards[_selected] : null;
        // A popup on the card itself, not a dialog over the whole home.
        OpenPicker(LibraryOrder.DisplayTitle(game, _prefs.Art.GetValueOrDefault(game.Id)), SourceLabel(game.Source), options,
            card ?? origin, anchor: card);
    }

    private async Task AddGameAsync()
    {
        if (_activeDialog is not null) return;
        await RunAsync(async () =>
        {
            var file = await PickFileAsync(".exe");
            if (file is null) return;
            var game = new Game { Id = Guid.NewGuid().ToString("N"), Path = file, Source = GameSource.Manual };
            var fields = new StackPanel { Spacing = 14 };
            var name = Field(fields, Strings.Get("FieldName"), game.Title);
            var arguments = Field(fields, Strings.Get("FieldArgumentsOptional"), "");
            if (!await ShowDialogAsync(Strings.Get("AddGame"), fields, Strings.Get("Add"))) return;
            game.Name = name.Text.Trim();
            game.Arguments = arguments.Text.Trim();
            _prefs.Manual.Add(game);
            _prefs.Hidden.Remove(game.Id);
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
        if (!game.IsManual)
            fields.Children.Insert(0, new TextBlock
            {
                Text = Strings.Format("FoundGameNote", SourceLabel(game.Source)), TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeManager.Brush("TextMutedBrush"), FontSize = 15
            });
        if (!await ShowDialogAsync(Strings.Get("EditGame"), fields, Strings.Get("Save"))) return;
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

    /// <summary>Search the catalog by another title, preview cover and background, and apply on confirm.</summary>
    private async Task SearchArtworkAsync(Game game)
    {
        _artworkLookup?.Cancel();
        var panel = new StackPanel { Spacing = 14, Width = 560 };
        var query = Field(panel, Strings.Get("FieldSearchTitle"), LibraryOrder.SearchTitle(game, _prefs.Art.GetValueOrDefault(game.Id)));
        var search = new Button { Content = Strings.Get("Search"), Style = ThemeManager.Style("DialogButton"), HorizontalAlignment = HorizontalAlignment.Left };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Brush("TextMutedBrush"), FontSize = 15 };
        var results = new ListView { MinHeight = 60, MaxHeight = 220, SelectionMode = ListViewSelectionMode.Single };
        var previewTitle = new TextBlock { FontSize = 17, TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Brush("TextBrush") };
        var media = new Grid { ColumnSpacing = 14, Height = 150 };
        media.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        media.ColumnDefinitions.Add(new ColumnDefinition());
        var coverImage = PreviewImage(media, 0);
        var backgroundImage = PreviewImage(media, 1);
        var preview = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        preview.Children.Add(previewTitle);
        preview.Children.Add(media);
        panel.Children.Add(search);
        panel.Children.Add(message);
        panel.Children.Add(results);
        panel.Children.Add(preview);

        using var cancellation = new CancellationTokenSource();
        CancellationTokenSource? previewRequest = null;
        (CatalogMatch Match, CatalogEntry Entry, string? Cover, string? Background)? chosen = null;
        var dialog = CreateDialog(Strings.Get("SearchArtwork"), panel, Strings.Get("Apply"));
        dialog.IsPrimaryButtonEnabled = false;

        void Discard()
        {
            if (chosen is { } old)
            {
                if (old.Cover is not null) File.Delete(old.Cover);
                if (old.Background is not null) File.Delete(old.Background);
            }
            chosen = null;
        }

        results.SelectionChanged += async (_, _) =>
        {
            previewRequest?.Cancel();
            dialog.IsPrimaryButtonEnabled = false;
            coverImage.Source = backgroundImage.Source = null;
            Discard();
            if (results.SelectedItem is not CatalogMatch match) { preview.Visibility = Visibility.Collapsed; return; }
            var request = previewRequest = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            preview.Visibility = Visibility.Visible;
            previewTitle.Text = Strings.Get("LoadingPreview");
            try
            {
                var entry = await _catalog.GetAsync(match, Strings.Language, request.Token);
                var cover = await TryDownload(entry.Cover, request.Token);
                var background = await TryDownload(entry.Background, request.Token);
                if (request.IsCancellationRequested)
                {
                    if (cover is not null) File.Delete(cover);
                    if (background is not null) File.Delete(background);
                    return;
                }
                chosen = (match, entry, cover, background);
                coverImage.Source = LoadImage(cover, 300);
                backgroundImage.Source = LoadImage(background, 800);
                previewTitle.Text = match.Label;
                dialog.IsPrimaryButtonEnabled = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { previewTitle.Text = Strings.Format("PreviewFailed", ex.Message); }
        };
        async Task Search()
        {
            search.IsEnabled = false;
            results.ItemsSource = null;
            message.Text = Strings.Get("Searching");
            try
            {
                var found = await _catalog.SearchAsync(query.Text.Trim(), cancellation.Token);
                results.ItemsSource = found;
                message.Text = found.Count > 0 ? Strings.Get("PickResult") : Strings.Get("NoResults");
                if (found.Count == 1) results.SelectedIndex = 0;
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
        dialog.Opened += async (_, _) => await Search();
        var apply = await ShowDialogAsync(dialog);
        cancellation.Cancel();
        coverImage.Source = backgroundImage.Source = null;
        if (apply && chosen is { } pick)
        {
            try { ApplyArtwork(game, pick.Match, pick.Entry, pick.Cover, pick.Background); }
            finally { Discard(); }
        }
        else Discard();
    }

    private static Image PreviewImage(Grid grid, int column)
    {
        var frame = new Grid { Background = ThemeManager.Brush("SurfaceBrush") };
        Corners.SetSize(frame, CornerSize.Small);
        var image = new Image { Stretch = Stretch.UniformToFill };
        frame.Children.Add(image);
        Grid.SetColumn(frame, column);
        grid.Children.Add(frame);
        return image;
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

    private static TextBox Field(Panel panel, string title, string value)
    {
        var field = new TextBox { Header = title, Text = value, Style = ThemeManager.Style("TextField") };
        panel.Children.Add(field);
        return field;
    }

    // ───────── Dialogs ─────────

    private ContentDialog CreateDialog(string title, object content, string confirm)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = title, Content = content, PrimaryButtonText = confirm,
            CloseButtonText = Strings.Get("Cancel"), DefaultButton = ContentDialogButton.None,
            RequestedTheme = ThemeManager.ElementTheme,
            PrimaryButtonStyle = ThemeManager.Style("DialogPrimaryButton"),
            CloseButtonStyle = ThemeManager.Style("DialogButton"),
            FontFamily = (FontFamily)Application.Current.Resources["AppFont"]
        };
        Corners.SetSize(dialog, CornerSize.Large);
        return dialog;
    }

    private Task<bool> ShowDialogAsync(string title, FrameworkElement content, string confirm) =>
        ShowDialogAsync(CreateDialog(title, content, confirm));

    private Task<bool> ConfirmAsync(string title, string body, string confirm) =>
        ShowDialogAsync(CreateDialog(title, new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, FontSize = 17, MaxWidth = 520 }, confirm));

    private async Task<bool> ShowDialogAsync(ContentDialog dialog)
    {
        if (_activeDialog is not null || Root.XamlRoot is null) return false;
        _activeDialog = dialog;
        Sounds.Play(UiSound.Open);
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        finally
        {
            _activeDialog = null;
            Sounds.Play(UiSound.Back);
        }
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

    // ───────── Picker ─────────

    private void OpenPicker(string title, string subtitle, IReadOnlyList<PickerOption> options, Control? origin, FrameworkElement? anchor = null)
    {
        if (_activeDialog is not null) return;
        _pickerOrigin = origin;
        var popup = anchor is not null;
        PickerTitle.FontSize = popup ? 22 : 32;
        PickerPanel.Width = popup ? 440 : 760;
        PickerPanel.Padding = popup ? new Thickness(18, 18, 18, 18) : new Thickness(32, 30, 32, 30);
        Picker.Background = popup ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : ThemeManager.Brush("DimBrush");
        // A card's popup is just its menu: the game's name is already on the card and in the hero.
        PickerTitle.Text = title;
        PickerTitle.Visibility = popup ? Visibility.Collapsed : Visibility.Visible;
        PickerSubtitle.Text = subtitle;
        PickerSubtitle.Visibility = !popup && subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PickerHeader.Visibility = PickerTitle.Visibility;
        PickerGrid.RowSpacing = popup ? 0 : 20;
        PickerOptions.Spacing = popup ? 6 : 10;
        PickerOptions.Children.Clear();
        Button? first = null, selected = null;
        // The icon column only when some option has an icon, so plain choices line up with the title.
        var iconColumn = options.Any(option => option.Glyph.Length > 0);
        foreach (var option in options)
        {
            var row = new Grid { ColumnSpacing = iconColumn ? 18 : 0 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(iconColumn ? 24 : 0) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            var foreground = ThemeManager.Brush(option.Danger ? "AccentBrush" : "TextBrush");
            if (option.Glyph.Length > 0)
                row.Children.Add(new FontIcon { Glyph = option.Glyph, FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Foreground = foreground });
            var label = new TextBlock
            {
                Text = option.Label, FontSize = 20, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = foreground
            };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            if (option.Selected)
            {
                var check = new FontIcon { Glyph = "", FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeManager.Brush("AccentBrush") };
                Grid.SetColumn(check, 2);
                row.Children.Add(check);
            }
            var button = new Button { Style = ThemeManager.Style("OptionButton"), Content = row };
            if (popup)
            {
                button.Height = 52;
                label.FontSize = 18;
            }
            AutomationProperties.SetName(button, option.Label);
            if (option.Selected) AutomationProperties.SetItemStatus(button, Strings.Get("Selected"));
            button.Click += async (_, _) =>
            {
                Sounds.Play(UiSound.Confirm);
                ClosePicker(restoreFocus: true);
                if (option.Action is not null) await RunAsync(option.Action);
            };
            PickerOptions.Children.Add(button);
            first ??= button;
            if (option.Selected) selected = button;
        }
        PlacePickerPanel(anchor);
        Picker.Visibility = Visibility.Visible;
        Sounds.Play(UiSound.Open);
        Motion.Fade(Picker, 1, 160, _prefs.ReduceMotion);
        var target = selected ?? first;
        DispatcherQueue.TryEnqueue(() => target?.Focus(_input.Mode == Core.Input.InputMode.Controller ? FocusState.Keyboard : FocusState.Programmatic));
    }

    /// <summary>In the middle of the screen, or just above the card it belongs to (below it when there is no room).</summary>
    private void PlacePickerPanel(FrameworkElement? anchor)
    {
        if (anchor is null || anchor.ActualWidth <= 0)
        {
            PickerPanel.HorizontalAlignment = HorizontalAlignment.Center;
            PickerPanel.VerticalAlignment = VerticalAlignment.Center;
            PickerPanel.Margin = new Thickness(0);
            return;
        }
        var bounds = anchor.TransformToVisual(Stage).TransformBounds(new Windows.Foundation.Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
        PickerPanel.Measure(new Windows.Foundation.Size(PickerPanel.Width, double.PositiveInfinity));
        var height = PickerPanel.DesiredSize.Height;
        var left = Math.Clamp(bounds.X, 24, Math.Max(24, Stage.ActualWidth - PickerPanel.Width - 24));
        var top = bounds.Y - height - 16;
        if (top < 24) top = Math.Min(bounds.Bottom + 16, Math.Max(24, Stage.ActualHeight - height - 24));
        PickerPanel.HorizontalAlignment = HorizontalAlignment.Left;
        PickerPanel.VerticalAlignment = VerticalAlignment.Top;
        PickerPanel.Margin = new Thickness(left, top, 0, 0);
    }

    /// <summary>A click outside the popup closes it.</summary>
    private void OnPickerBackgroundTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => ClosePicker();

    private void OnPickerPanelTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => e.Handled = true;

    private void ClosePicker(bool restoreFocus = true)
    {
        if (Picker.Visibility != Visibility.Visible) return;
        Picker.Visibility = Visibility.Collapsed;
        PickerOptions.Children.Clear();
        if (!restoreFocus) return;
        Sounds.Play(UiSound.Back);
        var origin = _pickerOrigin;
        _pickerOrigin = null;
        if (origin is not null && origin.Visibility == Visibility.Visible && IsInside(origin, ActiveLayer))
            origin.Focus(_input.Mode == Core.Input.InputMode.Controller ? FocusState.Keyboard : FocusState.Programmatic);
        else FocusFirst(ActiveLayer);
    }

    private void CloseOverlays()
    {
        _activeDialog?.Hide();
        ClosePicker(restoreFocus: false);
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
