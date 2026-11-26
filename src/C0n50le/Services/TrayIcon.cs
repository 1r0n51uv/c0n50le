using C0n50le.Core.Localization;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace C0n50le.Services;

/// <summary>The icon in the notification area: open the home, or quit. Adapted from Console Mode.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MenuFlyoutItem _open = new();
    private readonly MenuFlyoutItem _exit = new();

    public TrayIcon(Action open, Action exit)
    {
        _icon = new TaskbarIcon { ToolTipText = "c0n50le" };
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "c0n50le.ico");
        if (File.Exists(iconPath)) _icon.IconSource = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
        // A native popup menu: it runs each item's Command and never raises Click.
        _open.Command = new RelayCommand(open);
        _exit.Command = new RelayCommand(exit);
        var menu = new MenuFlyout();
        menu.Items.Add(_open);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(_exit);
        _icon.ContextFlyout = menu;
        _icon.LeftClickCommand = new RelayCommand(open);
        UpdateTexts();
        Strings.Changed += UpdateTexts;
        try { _icon.ForceCreate(); }
        catch (Exception ex) { AppLog.Write($"Tray: {ex.Message}"); }
    }

    private void UpdateTexts()
    {
        _open.Text = Strings.Get("TrayOpen");
        _exit.Text = Strings.Get("TrayExit");
    }

    public void Dispose()
    {
        Strings.Changed -= UpdateTexts;
        _icon.Dispose();
    }
}
