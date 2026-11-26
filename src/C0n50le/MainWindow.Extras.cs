using System.Diagnostics;
using System.Numerics;
using C0n50le.Core.Audio;
using C0n50le.Core.Input;
using C0n50le.Core.Localization;
using C0n50le.Core.Startup;
using C0n50le.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace C0n50le;

public sealed partial class MainWindow
{
    private bool IntroVisible => IntroLayer.Visibility == Visibility.Visible;
    private bool TourVisible => TourLayer.Visibility == Visibility.Visible;
    private int _tourIndex;
    private DispatcherQueueTimer? _tourTimer;
    private bool _checkingUpdates;

    // ───────── Intro ─────────

    /// <summary>The name lands on a black screen while the library loads, then the home fades in.</summary>
    private async void BeginIntro()
    {
        if (!Intro.ShouldShow(_prefs.Intro, AppWindow.IsVisible, _prefs.ReduceMotion || !Motion.Enabled(false)))
        {
            UpdateMusic(true);
            OfferTour();
            return;
        }
        var watch = Stopwatch.StartNew();
        IntroLayer.Visibility = Visibility.Visible;
        Sounds.Play(UiSound.Boot);
        AnimateIntro();
        while (!_loadedOnce && watch.Elapsed < Intro.Longest) await Task.Delay(100);
        var remaining = Intro.StillToShow(watch.Elapsed);
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
        Motion.Fade(IntroLayer, 0, 450, false);
        await Task.Delay(460);
        IntroLayer.Visibility = Visibility.Collapsed;
        ElementCompositionPreview.GetElementVisual(IntroLayer).Opacity = 1;
        UpdateMusic(IsForeground);
        FocusSelection();
        OfferTour();
    }

    private void AnimateIntro()
    {
        var word = ElementCompositionPreview.GetElementVisual(IntroWord);
        var compositor = word.Compositor;
        IntroWord.Opacity = 1;
        word.Opacity = 0;
        word.CenterPoint = new Vector3((float)IntroWord.ActualWidth / 2, (float)IntroWord.ActualHeight / 2, 0);
        IntroWord.SizeChanged += (_, _) =>
            word.CenterPoint = new Vector3((float)IntroWord.ActualWidth / 2, (float)IntroWord.ActualHeight / 2, 0);
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(.16f, 1f), new Vector2(.3f, 1f));
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0, new Vector3(1.28f, 1.28f, 1));
        scale.InsertKeyFrame(1, Vector3.One, ease);
        scale.Duration = TimeSpan.FromMilliseconds(1100);
        word.StartAnimation("Scale", scale);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = TimeSpan.FromMilliseconds(900);
        word.StartAnimation("Opacity", fade);

        var bar = ElementCompositionPreview.GetElementVisual(IntroBar);
        IntroBar.Width = 260;
        bar.CenterPoint = new Vector3(130, 4, 0);
        var grow = compositor.CreateVector3KeyFrameAnimation();
        grow.InsertKeyFrame(0, new Vector3(0, 1, 1));
        grow.InsertKeyFrame(0.45f, new Vector3(0, 1, 1));
        grow.InsertKeyFrame(1, Vector3.One, ease);
        grow.Duration = TimeSpan.FromMilliseconds(1500);
        bar.StartAnimation("Scale", grow);

        var tagline = ElementCompositionPreview.GetElementVisual(IntroTagline);
        IntroTagline.Opacity = 1;
        tagline.Opacity = 0;
        var late = compositor.CreateScalarKeyFrameAnimation();
        late.InsertKeyFrame(0, 0);
        late.InsertKeyFrame(0.6f, 0);
        late.InsertKeyFrame(1, 1);
        late.Duration = TimeSpan.FromMilliseconds(1900);
        tagline.StartAnimation("Opacity", late);
    }

    // ───────── Tour ─────────

    private void OfferTour()
    {
        if (Tour.ShouldOffer(_prefs.TourDone, _session is not null) && AppWindow.IsVisible) StartTour(force: false);
    }

    private void StartTour(bool force)
    {
        if (TourVisible || (!force && _prefs.TourDone) || IntroVisible) return;
        if (SystemPage.Visibility == Visibility.Visible) CloseSystem();
        ClosePicker(restoreFocus: false);
        _tourIndex = 0;
        TourLayer.Visibility = Visibility.Visible;
        ShowTourStop();
    }

    private void ShowTourStop()
    {
        var stop = Tour.Stops[_tourIndex];
        TourStep.Text = Strings.Format("TourStep", _tourIndex + 1, Tour.Stops.Count);
        TourTitle.Text = Strings.Get(Tour.TitleKey(stop));
        var family = Home.Family;
        TourBody.Text = Strings.Format(Tour.BodyKey(stop), _prefs.KeyboardShortcut.Length > 0 ? $"Ctrl+Alt+{_prefs.KeyboardShortcut}" : "—",
            _prefs.ControllerShortcut != 0 ? ButtonGlyphs.Combination(_prefs.ControllerShortcut, family) : "—",
            ButtonGlyphs.Options(family));
        TourNext.Content = Strings.Get(Tour.Step(_tourIndex, 1) is null ? "TourFinish" : "TourNext");
        TourSkip.Content = Strings.Get("TourSkip");
        TourSkip.Visibility = Tour.Step(_tourIndex, 1) is null ? Visibility.Collapsed : Visibility.Visible;
        Sounds.Play(UiSound.Move);

        FrameworkElement? target = stop switch
        {
            TourStop.Library => CardsScroll,
            TourStop.CardOptions => _cards.Count > 0 && _selected >= 0 ? _cards[_selected] : CardsScroll,
            TourStop.System => SystemPill,
            _ => null
        };
        if (Tour.IsCentered(stop) || target is null || target.ActualWidth <= 0)
        {
            TourHighlight.Visibility = Visibility.Collapsed;
            TourCard.HorizontalAlignment = HorizontalAlignment.Center;
            TourCard.VerticalAlignment = VerticalAlignment.Center;
            TourCard.Margin = new Thickness(0);
        }
        else
        {
            var bounds = target.TransformToVisual(Stage).TransformBounds(new Windows.Foundation.Rect(0, 0, target.ActualWidth, target.ActualHeight));
            const double pad = 12;
            TourHighlight.Visibility = Visibility.Visible;
            Microsoft.UI.Xaml.Controls.Canvas.SetLeft(TourHighlight, bounds.X - pad);
            Microsoft.UI.Xaml.Controls.Canvas.SetTop(TourHighlight, bounds.Y - pad);
            TourHighlight.Width = Math.Min(bounds.Width + 2 * pad, Stage.ActualWidth - bounds.X + pad);
            TourHighlight.Height = bounds.Height + 2 * pad;
            // The card sits beside what it points at, on the other half of the screen, never over the hero's text:
            // under a target at the top, else at the right edge above the row of cards.
            var targetAtTop = bounds.Y + bounds.Height / 2 < Stage.ActualHeight / 3;
            var targetOnLeft = bounds.X + bounds.Width / 2 < Stage.ActualWidth / 2 || bounds.Width > Stage.ActualWidth * 0.6;
            TourCard.HorizontalAlignment = targetAtTop
                ? (targetOnLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right)
                : (targetOnLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left);
            TourCard.VerticalAlignment = VerticalAlignment.Top;
            var top = targetAtTop ? bounds.Bottom + 40 : Math.Clamp(Math.Min(bounds.Y, CardsScroll.TransformToVisual(Stage)
                .TransformPoint(new Windows.Foundation.Point(0, 0)).Y) - 360, 120, Math.Max(120, Stage.ActualHeight - 420));
            TourCard.Margin = new Thickness(72, top, 72, 0);
        }
        TourNext.Focus(_input.Mode == InputMode.Controller ? FocusState.Keyboard : FocusState.Programmatic);
        _tourTimer ??= CreateTourTimer();
        _tourTimer.Stop();
        _tourTimer.Interval = Tour.DwellTime(TourTitle.Text, TourBody.Text) + TimeSpan.FromSeconds(6);
        _tourTimer.Start();
    }

    private DispatcherQueueTimer CreateTourTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) => { if (TourVisible) TourAdvance(); };
        return timer;
    }

    private void TourAdvance()
    {
        if (Tour.Step(_tourIndex, 1) is { } next)
        {
            _tourIndex = next;
            ShowTourStop();
        }
        else EndTour();
    }

    private void EndTour()
    {
        _tourTimer?.Stop();
        TourLayer.Visibility = Visibility.Collapsed;
        _prefs.TourDone = true;
        Save();
        Sounds.Play(UiSound.Back);
        FocusSelection();
    }

    private void OnTourNextClick(object sender, RoutedEventArgs e) => TourAdvance();

    private void OnTourSkipClick(object sender, RoutedEventArgs e) => EndTour();

    // ───────── Updates ─────────

    private async Task CheckUpdatesAsync(bool interactive)
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        try
        {
            if (interactive) ShowToast(Strings.Get("CheckingUpdates"));
            var update = await Updater.FindAsync(_prefs.BetaUpdates, _paths.Installed);
            if (update is null)
            {
                if (interactive) ShowToast(Strings.Format("UpToDate", Updater.CurrentVersion));
                return;
            }
            AppLog.Write($"Aggiornamento disponibile: {update.Tag}");
            if (_session is not null || !IsForeground || IsOverlayOpen || IntroVisible)
            {
                ShowToast(Strings.Format("UpdateAvailable", update.Tag));
                if (!interactive) return;
            }
            if (!await ConfirmAsync(Strings.Get("UpdateTitle"), Strings.Format("UpdateBody", update.Tag), Strings.Get("Install"))) return;
            ShowToast(Strings.Get("Downloading"));
            await Updater.InstallAsync(update, _paths.Installed);
            ExitApp();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Aggiornamenti: {ex.Message}");
            if (interactive) ShowToast(Strings.Format("UpdateFailed", ex.Message));
        }
        finally { _checkingUpdates = false; }
    }
}
