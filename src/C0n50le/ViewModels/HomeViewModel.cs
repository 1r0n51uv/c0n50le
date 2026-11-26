using C0n50le.Core.Input;
using C0n50le.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace C0n50le.ViewModels;

/// <summary>What the home shows around the row of cards: the hero of the selected game, the top bar and the hints.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    [ObservableProperty] private string _heroTitle = "";
    [ObservableProperty] private string _heroDetails = "";
    public bool HasHeroDetails => HeroDetails.Length > 0;
    public bool HasHeroDescription => HeroDescription.Length > 0;
    [ObservableProperty] private string _heroDescription = "";
    [ObservableProperty] private string _heroStatus = "";
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private bool _isRunning;

    [ObservableProperty] private string _clock = "";
    [ObservableProperty] private string _inputLabel = "";
    [ObservableProperty] private string _inputGlyph = "";
    [ObservableProperty] private string _systemLabel = "";

    [ObservableProperty] private InputMode _mode = InputMode.MouseKeyboard;
    [ObservableProperty] private PadFamily _family = PadFamily.Xbox;

    public bool IsControllerMode => Mode == InputMode.Controller;
    public bool IsMouseMode => Mode == InputMode.MouseKeyboard;

    public string ConfirmGlyph => ButtonGlyphs.Confirm(Family);
    public string BackGlyph => ButtonGlyphs.Back(Family);
    public string OptionsGlyph => ButtonGlyphs.Options(Family);
    public string MenuGlyph => ButtonGlyphs.Menu(Family);

    public string HintPlay => IsRunning ? Strings.Get("Resume") : Strings.Get("Play");
    public string HintOptions => Strings.Get("HintOptions");
    public string HintSystem => Strings.Get("System");
    public string HintDesktop => Strings.Get("HintDesktop");
    public string HintSelect => Strings.Get("HintSelect");
    public string HintBack => Strings.Get("HintBack");
    public string HintCategory => Strings.Get("HintCategory");

    partial void OnModeChanged(InputMode value)
    {
        OnPropertyChanged(nameof(IsControllerMode));
        OnPropertyChanged(nameof(IsMouseMode));
        InputGlyph = value == InputMode.Controller ? "" : "";
    }


    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(HintPlay));

    partial void OnHeroDetailsChanged(string value) => OnPropertyChanged(nameof(HasHeroDetails));

    partial void OnHeroDescriptionChanged(string value) => OnPropertyChanged(nameof(HasHeroDescription));
    partial void OnFamilyChanged(PadFamily value) => RefreshTexts();

    /// <summary>Raises every computed text again, after a language or pad change.</summary>
    public void RefreshTexts()
    {
        foreach (var name in new[]
                 {
                     nameof(ConfirmGlyph), nameof(BackGlyph), nameof(OptionsGlyph), nameof(MenuGlyph), nameof(HintPlay),
                     nameof(HintOptions), nameof(HintSystem), nameof(HintDesktop), nameof(HintSelect), nameof(HintBack),
                     nameof(HintCategory)
                 })
            OnPropertyChanged(name);
    }
}
