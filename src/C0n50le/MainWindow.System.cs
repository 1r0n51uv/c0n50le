using System.Diagnostics;
using C0n50le.Core.Audio;
using C0n50le.Core.Diagnostics;
using C0n50le.Core.Input;
using C0n50le.Core.Library;
using C0n50le.Core.Localization;
using C0n50le.Core.Theming;
using C0n50le.Services;
using C0n50le.Ui;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace C0n50le;

/// <summary>Settings, like tvOS Settings: a sidebar of categories, rows with their value on the right.</summary>
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
        SystemPage.Visibility = Visibility.Visible;
        HomePage.Visibility = Visibility.Collapsed;
        TopBar.Visibility = HomeHints.Visibility = Visibility.Collapsed;
        PageDim.Opacity = 1;
        Motion.Fade(PageDim, 1, 220, _prefs.ReduceMotion);
        RenderSystem();
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FocusCategory);
    }

    private void CloseSystem()
    {
        if (SystemPage.Visibility != Visibility.Visible) return;
        Sounds.Play(UiSound.Back);
        SystemPage.Visibility = Visibility.Collapsed;
        HomePage.Visibility = Visibility.Visible;
        TopBar.Visibility = Visibility.Visible;
        HomeHints.Visibility = Home.IsControllerMode ? Visibility.Visible : Visibility.Collapsed;
        Motion.Fade(PageDim, 0, 220, _prefs.ReduceMotion);
        _padName = _padButtons = null;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FocusSelection);
    }

    private void GoBackInSystem()
    {
        var focused = Root.XamlRoot is null ? null : FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        if (focused is not null && IsInside(focused, Rows))
        {
            Sounds.Play(UiSound.Back);
            FocusCategory();
            return;
        }
        CloseSystem();
    }

    private void OnSystemBackClick(object sender, RoutedEventArgs e) => GoBackInSystem();

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
        if (Categories.Children.Count > _category && Categories.Children[_category] is Button button) button.Focus(FocusState.Keyboard);
    }

    private void RenderSystem()
    {
        SystemTitle.Text = Strings.Get("System");
        RenderCategories();
        RenderRows();
    }

    /// <summary>The sidebar: a glyph in a circle and the name; the chosen category stays bright.</summary>
    private void RenderCategories()
    {
        Categories.Children.Clear();
        for (var index = 0; index < SystemCategories.Length; index++)
        {
            var position = index;
            var (key, glyph) = SystemCategories[index];
            var selected = index == _category;
            var row = new Grid { ColumnSpacing = 18 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var circle = new Grid { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = ThemeManager.Brush("GlyphCircleBrush") };
            circle.Children.Add(new FontIcon { Glyph = glyph, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(circle);
            var label = new TextBlock { Text = Strings.Get(key), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var button = new Button { Style = ThemeManager.Style("TvListItem"), Content = row, Padding = new Thickness(14, 0, 20, 0), Opacity = selected ? 1 : 0.62 };
            AutomationProperties.SetName(button, Strings.Get(key));
            if (selected) AutomationProperties.SetItemStatus(button, Strings.Get("Selected"));
            // Moving over a category shows its settings, like tvOS.
            button.GotFocus += (_, _) =>
            {
                button.Opacity = 1;
                if (_category == position) return;
                _category = position;
                RenderSystem();
                FocusCategory();
            };
            button.LostFocus += (_, _) => button.Opacity = _category == position ? 1 : 0.62;
            button.Click += (_, _) =>
            {
                Sounds.Play(UiSound.Confirm);
                Rows.Children.OfType<Button>().FirstOrDefault()?.Focus(FocusState.Keyboard);
            };
            button.KeyDown += (_, args) =>
            {
                if (args.Key != Windows.System.VirtualKey.Right) return;
                Rows.Children.OfType<Button>().FirstOrDefault()?.Focus(FocusState.Keyboard);
                args.Handled = true;
            };
            Categories.Children.Add(button);
        }
    }

    private void RenderRows(int? focusRow = null)
    {
        Rows.Children.Clear();
        _padName = _padButtons = null;
        CategoryTitle.Text = Strings.Get(SystemCategories[_category].Key);
        RowFooter.Text = "";
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
        if (focusRow is { } index && index >= 0 && index < Rows.Children.Count && Rows.Children[index] is Button button)
            button.Focus(FocusState.Keyboard);
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
        var folder = _prefs.GamesFolder.Length > 0 ? Path.GetFileName(_prefs.GamesFolder.TrimEnd('\\')) : Strings.Get("NotSet");
        ActionRow("GamesFolder", "GamesFolderBody", folder, () =>
        {
            var entries = new List<MenuEntry>
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
                entries.Add(new(Strings.Get("ClearFolder"), "", Action: async () =>
                {
                    _prefs.GamesFolder = "";
                    Save();
                    Rerender();
                    await LoadLibraryAsync();
                }));
            OpenMenu(entries, FocusedButton(), FocusedButton());
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
            var entries = hidden.Select(game => new MenuEntry(LibraryOrder.DisplayTitle(game, _prefs.Art.GetValueOrDefault(game.Id)), "",
                Action: async () =>
                {
                    _prefs.Hidden.Remove(game.Id);
                    Save();
                    await LoadLibraryAsync();
                    Rerender();
                })).ToList();
            OpenMenu(entries, FocusedButton(), FocusedButton());
            return Task.CompletedTask;
        });
    }

    private void ScreenRows()
    {
        var displays = Displays.Choices((number, primary, size) => Strings.Format(primary ? "ScreenPrimary" : "ScreenNumber", number, size));
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
        _padButtons.Text = pad.Held == 0 ? Strings.Get("ControllerTestIdle")
            : ButtonGlyphs.Combination(pad.Held, pad.Family) is { Length: > 0 } text ? text : DirectionText(pad.Held);
    }

    private static string DirectionText(int held) => string.Join(" ", new[]
    {
        (held & ControllerButtons.Up) != 0 ? "↑" : "", (held & ControllerButtons.Down) != 0 ? "↓" : "",
        (held & ControllerButtons.Left) != 0 ? "←" : "", (held & ControllerButtons.Right) != 0 ? "→" : ""
    }.Where(part => part.Length > 0));

    private void LookRows()
    {
        var themes = ThemeCatalog.Ids;
        var themeNames = themes.Select(id => Strings.Get("Theme_" + id)).ToArray();
        var currentTheme = themes.ToList().IndexOf(_prefs.Theme);
        Choice("Theme", "ThemeBody", themeNames[Math.Max(0, currentTheme)], themeNames, currentTheme, index =>
        {
            _prefs.Theme = themes[index];
            ApplyTheme();
        });
        var depths = ThemeCatalog.Depths;
        var depthNames = depths.Select(depth => Strings.Get("Glass_" + depth)).ToArray();
        Choice("LiquidGlass", "LiquidGlassBody", depthNames[Math.Clamp(_prefs.Glass, 0, depths.Count - 1)], depthNames, _prefs.Glass, index =>
        {
            _prefs.Glass = (int)depths[index];
            ApplyTheme();
        });
        Toggle("ShowSystemCard", "ShowSystemCardBody", _prefs.ShowSystemCard, on => { _prefs.ShowSystemCard = on; RenderCards(); });
        Toggle("ReduceMotion", "ReduceMotionBody", _prefs.ReduceMotion, on => { _prefs.ReduceMotion = on; Motion.Reduce = on; });
        Toggle("InterfaceSounds", "InterfaceSoundsBody", _prefs.Sounds, on => { _prefs.Sounds = on; Sounds.Enabled = on; });
        Toggle("HomeMusic", "HomeMusicBody", _prefs.Music, on => { _prefs.Music = on; UpdateMusic(IsForeground); });
        Toggle("IntroSetting", "IntroSettingBody", _prefs.Intro, on => _prefs.Intro = on);
        ActionRow("TourAgain", "TourAgainBody", "", () =>
        {
            CloseSystem();
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => StartTour(force: true));
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
            if (_session is not null && await AlertAsync(Strings.Get("ExitApp"), Strings.Get("ExitWhileRunning"), null,
                    Strings.Get("ExitApp"), Strings.Get("Cancel")) != 0) return;
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

    /// <summary>
    /// A settings row like tvOS: the name on the left, the value on the right in secondary text. Its description shows
    /// under the list while it is focused, so the rows stay one line.
    /// </summary>
    private Button Row(string titleKey, string bodyKey, string value, bool chevron)
    {
        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = Strings.Get(titleKey), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        var valueText = new TextBlock
        {
            Text = value, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460, TextTrimming = TextTrimming.CharacterEllipsis
        };
        right.Children.Add(valueText);
        if (chevron) right.Children.Add(new FontIcon { Glyph = "", FontSize = 18, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        var button = new Button { Style = ThemeManager.Style("TvListItem"), Content = grid, Padding = new Thickness(28, 0, 28, 0), Tag = valueText };
        AutomationProperties.SetName(button, Strings.Get(titleKey));
        AutomationProperties.SetHelpText(button, bodyKey.Length > 0 ? Strings.Get(bodyKey) : "");
        button.GotFocus += (_, _) =>
        {
            RowFooter.Text = bodyKey.Length > 0 ? Strings.Get(bodyKey) : "";
            button.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.4, AnimationDesired = !_prefs.ReduceMotion });
        };
        Rows.Children.Add(button);
        return button;
    }

    /// <summary>On/off as text, as tvOS Settings does: pressing it switches.</summary>
    private void Toggle(string titleKey, string bodyKey, bool on, Action<bool> change)
    {
        var button = Row(titleKey, bodyKey, Strings.Get(on ? "On" : "Off"), chevron: false);
        AutomationProperties.SetItemStatus(button, Strings.Get(on ? "On" : "Off"));
        button.Click += (_, _) =>
        {
            Sounds.Play(UiSound.Confirm);
            change(!on);
            Save();
            Rerender();
        };
    }

    /// <summary>A choice opens a context menu beside the row, with the current one ticked.</summary>
    private void Choice(string titleKey, string bodyKey, string current, string[] options, int selected, Action<int> apply)
    {
        var button = Row(titleKey, bodyKey, current, chevron: true);
        button.Click += (_, _) =>
        {
            Sounds.Play(UiSound.Confirm);
            var row = FocusedRow();
            var entries = options.Select((label, index) => new MenuEntry(label, Checked: index == selected, Action: () =>
            {
                apply(index);
                Save();
                RenderSystem();
                RenderRows(Math.Max(0, row));
                return Task.CompletedTask;
            })).ToList();
            OpenMenu(entries, button, button);
        };
    }

    private void ActionRow(string titleKey, string bodyKey, string value, Func<Task> action)
    {
        var button = Row(titleKey, bodyKey, value, chevron: true);
        button.Click += async (_, _) =>
        {
            Sounds.Play(UiSound.Confirm);
            await RunAsync(action);
        };
    }

    private TextBlock Info(string titleKey, string value)
    {
        var button = Row(titleKey, "", value, chevron: false);
        return (TextBlock)button.Tag;
    }

    private Control? FocusedButton() => Root.XamlRoot is null ? null : FocusManager.GetFocusedElement(Root.XamlRoot) as Control;

    // ───────── Settings that touch the whole window ─────────

    private void ApplyTheme()
    {
        ThemeManager.Use(_prefs.Theme, (GlassDepth)_prefs.Glass);
        Root.RequestedTheme = ThemeManager.ElementTheme;
        _backdropPath = null;
        RenderCards();
        RefreshHero(fetch: false);
    }

    private void RegisterKeyboardShortcut(bool report)
    {
        if (!ReturnHotkey.Register(_handle, _prefs.KeyboardShortcut) && report) ShowToast(Strings.Get("ShortcutInUse"));
    }
}
