namespace C0n50le.Core.Startup;

/// <summary>The stops of the first-run tour, in order.</summary>
public enum TourStop
{
    /// <summary>What c0n50le does; nothing highlighted.</summary>
    Welcome,
    /// <summary>The row of cards: one per game, the most recent first.</summary>
    Library,
    /// <summary>A card's options: edit, search the cover, hide.</summary>
    CardOptions,
    /// <summary>The way back to the home from a game.</summary>
    ReturnHome,
    /// <summary>System: every setting.</summary>
    System,
    /// <summary>Ready; how to see the tour again.</summary>
    Done
}

/// <summary>What the tour shows. Pure, so it is tested; the view highlights each stop's part of the screen.</summary>
public static class Tour
{
    public static IReadOnlyList<TourStop> Stops { get; } = Enum.GetValues<TourStop>();

    public static bool IsCentered(TourStop stop) => stop is TourStop.Welcome or TourStop.ReturnHome or TourStop.Done;

    public static string TitleKey(TourStop stop) => $"Tour{stop}Title";
    public static string BodyKey(TourStop stop) => $"Tour{stop}Body";

    public static bool ShouldOffer(bool tourDone, bool gameRunning) => !tourDone && !gameRunning;

    /// <summary>How long a stop stays before moving on by itself: time to read it from the sofa, 7 to 16 s.</summary>
    public static TimeSpan DwellTime(string title, string body) =>
        TimeSpan.FromSeconds(Math.Clamp(3 + (title.Length + body.Length) / 16.0, 7, 16));

    public static int? Step(int index, int step)
    {
        var next = index + step;
        return next >= 0 && next < Stops.Count ? next : null;
    }
}

/// <summary>The opening: the name over a black screen while the home loads.</summary>
public static class Intro
{
    /// <summary>Shown at least this long, so the name lands even when loading is instant.</summary>
    public static readonly TimeSpan Minimum = TimeSpan.FromMilliseconds(2300);

    /// <summary>Never longer than this, even if loading hangs.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(7);

    /// <summary>Only when the window opens to be seen: a start to the tray skips it, and so does "reduce motion".</summary>
    public static bool ShouldShow(bool enabled, bool windowShown, bool reduceMotion) => enabled && windowShown && !reduceMotion;

    public static TimeSpan StillToShow(TimeSpan shownFor) => shownFor >= Minimum ? TimeSpan.Zero : Minimum - shownFor;
}
