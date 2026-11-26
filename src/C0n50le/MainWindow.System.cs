using System.Diagnostics;
using C0n50le.Core.Audio;
using C0n50le.Core.Diagnostics;
using C0n50le.Core.Input;
using C0n50le.Core.Library;
using C0n50le.Core.Localization;
using C0n50le.Core.Theming;
using C0n50le.Services;
using C0n50le.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace C0n50le;

public sealed partial class MainWindow
{
    private static readonly (string Key, string Glyph)[] SystemCategories =
    [
        ("CategoryLibrary", ""),
        ("CategoryScreen", ""),
        ("CategoryControls", ""),
        ("CategoryLook", ""),
        ("CategoryStart", ""),
        ("CategoryUpdates", ""),
        ("CategoryAbout", ""),
        ("CategoryLanguage", "")
    ];

    private int _category;
    private TextBlock? _padName, _padButtons;

    private void OpenSystem()
    {
        if (SystemPage.Visibility == Visibility.Visible || IsOverlayOpen) return;
        Sounds.Play(UiSound.Open);
        _hoverTimer?.Stop();
        SystemPage.Visibility = Visibility.Visible;
        HomePage.Visibility = Visibility.Collapsed;
        Motion.Fade(PageDim, 1, 220, _prefs.ReduceMotion);
        PageDim.Opacity = 1;
        RenderSystem();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FocusCategory);
    }

    private void CloseSystem()
    {
        if (SystemPage.Visibility != Visibility.Visible) return;
        Sounds.Play(UiSound.Back);
        SystemPage.Visibility = Visibility.Collapsed;
        HomePage.Visibility = Visibility.Visible;
        Motion.Fade(PageDim, 0, 220, _prefs.ReduceMotion);
        _padName = _padButtons = null;
        DispatcherQueue.TryEnqueue(FocusSelection);
    }

    private void OnSystemBackClick(object sender, RoutedEventArgs e) => CloseSystem();

    private void StepCategory(int step)
    {
        if (SystemPage.Visibility != Visibility.Visible || IsOverlayOpen) return;
        var next = Math.Clamp(_category + step, 0, SystemCategories.Length - 1);
        if (next == _category) return;
        _category = next;
        Sounds.Play(UiSound.Move);
        RenderSystem();
        FocusCategory();
    }

    private void FocusCategory()
    {
        if (Categories.Children.Count > _category && Categories.Children[_category] is Button button)
            button.Focus(_input.Mode == InputMode.Controller ? FocusState.Keyboard : FocusState.Programmatic);
    }

    private void RenderSystem()
    {
        RenderCategories();
        RenderRows();
    }

    private void RenderCategories()
    {
        Categories.Children.Clear();
        for (var index = 0; index < SystemCategories.Length; index++)
        {
            var position = index;
            var (key, glyph) = SystemCategories[index];
            var selected = index == _category;
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var indicator = new Border
            {
                Width = 4, Height = 28, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center,
                Background = ThemeManager.Brush("AccentBrush"), Opacity = selected ? 1 : 0
            };
            row.Children.Add(indicator);
            var icon = new FontIcon
            {
                Glyph = glyph, FontSize = 22, VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeManager.Brush(selected ? "AccentBrush" : "TextMutedBrush")
            };
            Grid.SetColumn(icon, 1);
            row.Children.Add(icon);
            var label = new TextBlock
            {
                Text = Strings.Get(key), FontSize = 20, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = ThemeManager.Brush("TextBrush")
            };
            Grid.SetColumn(label, 2);
            row.Children.Add(label);
            var button = new Button
            {
                Style = ThemeManager.Style("CategoryButton"), Content = row,
                Background = selected ? ThemeManager.Brush("TonalBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            };
            AutomationProperties.SetName(button, Strings.Get(key));
            if (selected) AutomationProperties.SetItemStatus(button, Strings.Get("Selected"));
            // Moving over a category shows its options, like a console's settings.
            button.GotFocus += (_, _) =>
            {
                if (_category == position) return;
                _category = position;
                RenderSystem();
                FocusCategory();
            };
            button.Click += (_, _) =>
            {
                _category = position;
                Sounds.Play(UiSound.Confirm);
                if (Rows.Children.OfType<Button>().FirstOrDefault() is { } first)
                    first.Focus(_input.Mode == InputMode.Controller ? FocusState.Keyboard : FocusState.Programmatic);
            };
            button.KeyDown += (_, args) =>
            {
                if (args.Key != Windows.System.VirtualKey.Right) return;
                if (Rows.Children.OfType<Button>().FirstOrDefault() is { } first) first.Focus(FocusState.Keyboard);
                args.Handled = true;
            };
            Categories.Children.Add(button);
        }
    }

    /// <summary>Redraws the rows and puts the focus back on the row it was on.</summary>
    private void RenderRows(int? focusRow = null)
    {
        Rows.Children.Clear();
        _padName = _padButtons = null;
        switch (SystemCategories[_category].Key)
        {
            case "CategoryLibrary": LibraryRows(); break;
            case "CategoryScreen": ScreenRows(); break;
            case "CategoryControls": ControlRows(); break;
            case "CategoryLook": LookRows(); break;
            case "CategoryStart": StartRows(); break;
            case "CategoryUpdates": UpdateRows(); break;
            case "CategoryAbout": AboutRows(); break;
            case "CategoryLanguage": LanguageRows(); break;
        }
        RowsScroll.ChangeView(null, 0, null, true);
        if (focusRow is { } index && index < Rows.Children.Count && Rows.Children[index] is Button button)
            button.Focus(_input.Mode == InputMode.Controller ? FocusState.Keyboard : FocusState.Programmatic);
    }

    private int FocusedRow()
    {
        var focused = Root.XamlRoot is null ? null : FocusManager.GetFocusedElement(Root.XamlRoot) as UIElement;
        return focused is null ? -1 : Rows.Children.IndexOf(focused);
    }

    private void Rerender() => RenderRows(Math.Max(0, FocusedRow()));

    // ───────── Categories ─────────

    private void LibraryRows()
    {
        var folder = _prefs.GamesFolder.Length > 0 ? _prefs.GamesFolder : Strings.Get("NotSet");
        ActionRow("GamesFolder", "GamesFolderBody", folder, () =>
        {
            var options = new List<PickerOption>
            {
                new(Strings.Get("ChooseFolder"), "", Action: async () =>
                {
                    if (await PickFolderAsync() is not { } path) return;
                    _prefs.GamesFolder = path;
                    Save();
                    Rerender();
                    await LoadLibraryAsync();
                })
            };
            if (_prefs.GamesFolder.Length > 0)
                options.Add(new(Strings.Get("ClearFolder"), "", Action: async () =>
                {
                    _prefs.GamesFolder = "";
                    Save();
                    Rerender();
                    await LoadLibraryAsync();
                }));
            OpenPicker(Strings.Get("GamesFolder"), folder, options, FocusedButton());
            return Task.CompletedTask;
        });
        ActionRow("AddGame", "AddGameBody", "", AddGameAsync);
        Toggle("ScanSteam", "ScanSteamBody", _prefs.ScanSteam, on => { _prefs.ScanSteam = on; _ = LoadLibraryAsync(); });
        Toggle("ScanEpic", "ScanEpicBody", _prefs.ScanEpic, on => { _prefs.ScanEpic = on; _ = LoadLibraryAsync(); });
        ActionRow("Rescan", "RescanBody", Strings.Format("GamesCount", _games.Count), async () =>
        {
            _fetched.Clear();
            await LoadLibraryAsync();
            ShowToast(Strings.Format("LibraryUpdated", _games.Count));
            Rerender();
        });
        var hidden = LibraryOrder.HiddenGames(_prefs, _detected);
        ActionRow("HiddenGames", "HiddenGamesBody", hidden.Count.ToString(), () =>
        {
            if (hidden.Count == 0) { ShowToast(Strings.Get("NoHiddenGames")); return Task.CompletedTask; }
            var options = hidden.Select(game => new PickerOption(LibraryOrder.DisplayTitle(game, _prefs.Art.GetValueOrDefault(game.Id)), "",
                Action: async () =>
                {
                    _prefs.Hidden.Remove(game.Id);
                    Save();
                    await LoadLibraryAsync();
                    Rerender();
                })).ToList();
            OpenPicker(Strings.Get("HiddenGames"), Strings.Get("ShowAgain"), options, FocusedButton());
            return Task.CompletedTask;
        });
    }

    private void ScreenRows()
    {
        var displays = Displays.Choices((number, primary, size) =>
            Strings.Format(primary ? "ScreenPrimary" : "ScreenNumber", number, size));
        var current = displays.FindIndex(display => display.Id == _prefs.Screen);
        var currentLabel = current >= 0 ? displays[current].Label : Strings.Get("ScreenAutomatic");
        Choice("HomeScreen", "HomeScreenBody", currentLabel,
            new[] { Strings.Get("ScreenAutomatic") }.Concat(displays.Select(display => display.Label)).ToArray(),
            current + 1, index =>
            {
                _prefs.Screen = index == 0 ? "" : displays[index - 1].Id;
                ShowHome();
            });
        var scales = UiScale.Choices.Select(value => $"{value * 100:0}%").ToArray();
        var scaleIndex = UiScale.Choices.ToList().FindIndex(value => Math.Abs(value - _prefs.UiScale) < 0.001);
        Choice("InterfaceScale", "InterfaceScaleBody", scales[Math.Max(0, scaleIndex)], scales, scaleIndex, index =>
        {
            _prefs.UiScale = UiScale.Choices[index];
            UpdateStage();
        });
    }

    private void ControlRows()
    {
        var keys = ShortcutChoices.Keyboard.Select(key => key.Length == 0 ? Strings.Get("Off") : $"Ctrl + Alt + {key}").ToArray();
        var keyIndex = ShortcutChoices.Keyboard.ToList().IndexOf(_prefs.KeyboardShortcut);
        Choice("KeyboardShortcut", "KeyboardShortcutBody", keys[Math.Max(0, keyIndex)], keys, keyIndex, index =>
        {
            _prefs.KeyboardShortcut = ShortcutChoices.Keyboard[index];
            RegisterKeyboardShortcut(report: true);
        });
        var family = Home.Family;
        var pads = ShortcutChoices.Controller.Select(mask => mask == 0 ? Strings.Get("Off") : ButtonGlyphs.Combination(mask, family)).ToArray();
        var padIndex = ShortcutChoices.Controller.ToList().IndexOf(_prefs.ControllerShortcut);
        Choice("ControllerShortcut", "ControllerShortcutBody", pads[Math.Max(0, padIndex)], pads, padIndex,
            index => _prefs.ControllerShortcut = ShortcutChoices.Controller[index]);
        _padName = Info("ControllerDetected", _lastPad.Connected ? _lastPad.Name : Strings.Get("NoController"));
        _padButtons = Info("ControllerTest", Strings.Get("ControllerTestIdle"));
    }

    private void UpdateControllerTest(ControllerState pad)
    {
        if (_padName is null || _padButtons is null) return;
        _padName.Text = pad.Connected ? pad.Name : Strings.Get("NoController");
        _padButtons.Text = pad.Held == 0 ? Strings.Get("ControllerTestIdle") : ButtonGlyphs.Combination(pad.Held, pad.Family) is { Length: > 0 } text
            ? text : DirectionText(pad.Held);
    }

    private static string DirectionText(int held) => string.Join(" ", new[]
    {
        (held & ControllerButtons.Up) != 0 ? "↑" : "", (held & ControllerButtons.Down) != 0 ? "↓" : "",
        (held & ControllerButtons.Left) != 0 ? "←" : "", (held & ControllerButtons.Right) != 0 ? "→" : ""
    }.Where(part => part.Length > 0));

    private void LookRows()
    {
        var themes = ThemeCatalog.All;
        var names = themes.Select(theme => Strings.Get(theme.NameKey)).ToArray();
        var current = themes.ToList().FindIndex(theme => theme.Id == _prefs.Theme);
        Choice("Theme", "ThemeBody", names[Math.Max(0, current)], names, current, index =>
        {
            _prefs.Theme = themes[index].Id;
            ApplyTheme();
        });
        Toggle("ShowSystemCard", "ShowSystemCardBody", _prefs.ShowSystemCard, on =>
        {
            _prefs.ShowSystemCard = on;
            if (_systemCard is not null) _systemCard.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        });
        Toggle("ReduceMotion", "ReduceMotionBody", _prefs.ReduceMotion, on => { _prefs.ReduceMotion = on; Motion.Reduce = on; });
        Toggle("InterfaceSounds", "InterfaceSoundsBody", _prefs.Sounds, on => { _prefs.Sounds = on; Sounds.Enabled = on; });
        Toggle("HomeMusic", "HomeMusicBody", _prefs.Music, on => { _prefs.Music = on; UpdateMusic(IsForeground); });
        Toggle("IntroSetting", "IntroSettingBody", _prefs.Intro, on => _prefs.Intro = on);
        ActionRow("TourAgain", "TourAgainBody", "", () =>
        {
            CloseSystem();
            DispatcherQueue.TryEnqueue(() => StartTour(force: true));
            return Task.CompletedTask;
        });
    }

    private void StartRows()
    {
        Toggle("StartWithWindows", "StartWithWindowsBody", StartupRegistration.IsEnabled, on =>
        {
            try { StartupRegistration.SetEnabled(on); }
            catch (Exception ex) { ShowToast(ex.Message); }
        });
        Toggle("OpenFullscreen", "OpenFullscreenBody", _prefs.OpenFullscreen, on => _prefs.OpenFullscreen = on);
        Toggle("CloseToTray", "CloseToTrayBody", _prefs.CloseToTray, on => _prefs.CloseToTray = on);
        ActionRow("BackToDesktop", "BackToDesktopBody", "", () => { HideHome(); return Task.CompletedTask; });
        ActionRow("ExitApp", "ExitAppBody", "", async () =>
        {
            if (_session is not null && !await ConfirmAsync(Strings.Get("ExitApp"), Strings.Get("ExitWhileRunning"), Strings.Get("ExitApp"))) return;
            ExitApp();
        });
    }

    private void UpdateRows()
    {
        Info("InstalledVersion", Updater.CurrentVersion);
        Toggle("CheckUpdates", "CheckUpdatesBody", _prefs.CheckUpdates, on => _prefs.CheckUpdates = on);
        Toggle("BetaUpdates", "BetaUpdatesBody", _prefs.BetaUpdates, on => _prefs.BetaUpdates = on);
        ActionRow("CheckNow", "CheckNowBody", "", () => CheckUpdatesAsync(interactive: true));
    }

    private void AboutRows()
    {
        Info("InstalledVersion", $"{Updater.CurrentVersion} · {(_paths.Installed ? Strings.Get("InstallSetup") : Strings.Get("InstallPortable"))}");
        ActionRow("DataFolder", "DataFolderBody", "", () =>
        {
            Directory.CreateDirectory(_paths.Data);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.Data}\"") { UseShellExecute = true });
            return Task.CompletedTask;
        });
        ActionRow("CopyDiagnostics", "CopyDiagnosticsBody", "", () =>
        {
            var report = DiagnosticsReport.Build(Updater.CurrentVersion, Environment.OSVersion.VersionString, _paths.Installed, _prefs, _games,
                _lastPad.Connected ? _lastPad.Name : "none", AppLog.Recent(40));
            var package = new DataPackage();
            package.SetText(report);
            Clipboard.SetContent(package);
            ShowToast(Strings.Get("DiagnosticsCopied"));
            return Task.CompletedTask;
        });
        Info("License", "GNU AGPL-3.0");
        Info("Credits", Strings.Get("CreditsBody"));
    }

    private void LanguageRows()
    {
        var labels = new[] { Strings.Get("LanguageAutomatic"), "Italiano", "English" };
        var values = new[] { "", Strings.Italian, Strings.English };
        var current = Array.IndexOf(values, _prefs.Language);
        Choice("Language", "LanguageBody", labels[Math.Max(0, current)], labels, current, index =>
        {
            _prefs.Language = values[index];
            Strings.Use(Strings.Resolve(_prefs.Language));
        });
    }

    // ───────── Rows ─────────

    /// <summary>Label and description on the left, the value on the right: the same row for every kind of setting.</summary>
    private Button Row(string titleKey, string bodyKey, FrameworkElement? value, bool chevron)
    {
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = Strings.Get(titleKey), FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Brush("TextBrush")
        });
        if (bodyKey.Length > 0)
            text.Children.Add(new TextBlock
            {
                Text = Strings.Get(bodyKey), FontSize = 15, LineHeight = 21, TextWrapping = TextWrapping.Wrap, MaxLines = 3,
                TextTrimming = TextTrimming.CharacterEllipsis, Foreground = ThemeManager.Brush("TextMutedBrush")
            });
        grid.Children.Add(text);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        if (value is not null) right.Children.Add(value);
        if (chevron)
            right.Children.Add(new FontIcon { Glyph = "", FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeManager.Brush("TextMutedBrush") });
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        var button = new Button { Style = ThemeManager.Style("RowButton"), Content = grid };
        AutomationProperties.SetName(button, Strings.Get(titleKey));
        button.GotFocus += (_, _) => button.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.4, AnimationDesired = !_prefs.ReduceMotion });
        Rows.Children.Add(button);
        return button;
    }

    private static TextBlock ValueText(string value) => new()
    {
        Text = value, FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Right, MaxWidth = 460, TextTrimming = TextTrimming.CharacterEllipsis,
        Foreground = ThemeManager.Brush("AccentBrush")
    };

    private void Toggle(string titleKey, string bodyKey, bool on, Action<bool> change)
    {
        var track = new Border
        {
            Width = 56, Height = 32, BorderThickness = new Thickness(on ? 0 : 2), VerticalAlignment = VerticalAlignment.Center,
            Background = on ? ThemeManager.Brush("AccentBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = ThemeManager.Brush("TextMutedBrush"), Padding = new Thickness(4)
        };
        Corners.SetSize(track, CornerSize.Small);
        var knob = new Border
        {
            Width = 20, Height = 20, HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(on ? 0 : 2, 0, 0, 0),
            Background = ThemeManager.Brush(on ? "OnAccentBrush" : "TextMutedBrush")
        };
        Corners.SetSize(knob, CornerSize.Tiny);
        track.Child = knob;
        var state = new TextBlock
        {
            Text = Strings.Get(on ? "On" : "Off"), FontSize = 17, VerticalAlignment = VerticalAlignment.Center, Width = 110,
            TextAlignment = TextAlignment.Right, Foreground = ThemeManager.Brush("TextMutedBrush")
        };
        var value = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        value.Children.Add(state);
        value.Children.Add(track);
        var button = Row(titleKey, bodyKey, value, chevron: false);
        AutomationProperties.SetItemStatus(button, Strings.Get(on ? "On" : "Off"));
        button.Click += (_, _) =>
        {
            Sounds.Play(UiSound.Confirm);
            change(!on);
            Save();
            Rerender();
        };
    }

    private void Choice(string titleKey, string bodyKey, string current, string[] options, int selected, Action<int> apply)
    {
        var button = Row(titleKey, bodyKey, ValueText(current), chevron: true);
        button.Click += (_, _) =>
        {
            Sounds.Play(UiSound.Confirm);
            var row = FocusedRow();
            var items = options.Select((label, index) => new PickerOption(label, Selected: index == selected, Action: () =>
            {
                apply(index);
                Save();
                RenderSystem();
                RenderRows(Math.Max(0, row));
                return Task.CompletedTask;
            })).ToList();
            OpenPicker(Strings.Get(titleKey), "", items, button);
        };
    }

    private void ActionRow(string titleKey, string bodyKey, string value, Func<Task> action)
    {
        var button = Row(titleKey, bodyKey, value.Length > 0 ? ValueText(value) : null, chevron: true);
        button.Click += async (_, _) =>
        {
            Sounds.Play(UiSound.Confirm);
            await RunAsync(action);
        };
    }

    private TextBlock Info(string titleKey, string value)
    {
        var text = ValueText(value);
        text.Foreground = ThemeManager.Brush("TextBrush");
        Row(titleKey, "", text, chevron: false);
        return text;
    }

    private Control? FocusedButton() => Root.XamlRoot is null ? null : FocusManager.GetFocusedElement(Root.XamlRoot) as Control;

    // ───────── Settings that touch the whole window ─────────

    private void ApplyTheme()
    {
        ThemeManager.Use(_prefs.Theme);
        Root.RequestedTheme = ThemeManager.ElementTheme;
        _backdropPath = null;
        RenderCards();
        RefreshHero(fetch: false);
    }

    private void RegisterKeyboardShortcut(bool report)
    {
        if (!ReturnHotkey.Register(_handle, _prefs.KeyboardShortcut) && report)
            ShowToast(Strings.Get("ShortcutInUse"));
    }
}
