using C0n50le.Core.Input;
using C0n50le.Core.Library;
using C0n50le.Core.Settings;

namespace C0n50le.Tests;

public sealed class PreferencesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "c0n50le-prefs-" + Guid.NewGuid().ToString("N"));
    private string File1 => Path.Combine(_folder, "preferences.json");

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    [Fact]
    public void Missing_file_gives_the_defaults()
    {
        var preferences = Preferences.Load(File1);
        Assert.Equal("dark", preferences.Theme);
        Assert.Equal(2, preferences.Glass);
        Assert.Equal("F11", preferences.KeyboardShortcut);
        Assert.Equal(ControllerButtons.View | ControllerButtons.Y, preferences.ControllerShortcut);
        Assert.True(preferences.ShowSystemCard);
        Assert.False(preferences.TourDone);
    }

    [Fact]
    public void Save_and_load_round_trip_without_leaving_a_temporary_file()
    {
        var preferences = new Preferences { GamesFolder = @"D:\Games", Theme = "light", Glass = 3, UiScale = 1.25, Language = "en-US" };
        var game = new Game { Id = "x", Name = "GTA 6", Path = @"D:\Games\GTA 6\GTA 6.exe" };
        preferences.Manual.Add(game);
        preferences.ArtFor(game).CatalogTitle = "Grand Theft Auto VI";
        preferences.MarkPlayed(game, DateTimeOffset.Parse("2026-11-26T18:00:00+01:00"));
        preferences.Save(File1);
        Assert.False(File.Exists(File1 + ".tmp"));
        var loaded = Preferences.Load(File1);
        Assert.Equal(@"D:\Games", loaded.GamesFolder);
        Assert.Equal("light", loaded.Theme);
        Assert.Equal(3, loaded.Glass);
        Assert.Equal(1.25, loaded.UiScale);
        Assert.Equal("Grand Theft Auto VI", loaded.Art["x"].CatalogTitle);
        Assert.Equal("x", loaded.LastPlayed);
        Assert.Equal(GameSource.Manual, Assert.Single(loaded.Manual).Source);
    }

    [Fact]
    public void Hand_edited_values_are_repaired()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(File1, """
            {"theme":"neon","glass":42,"uiScale":9,"keyboardShortcut":"F1","controllerShortcut":3,"language":"de-DE",
             "manual":[{"name":"No path"},{"path":"C:\\a.exe","source":"steam"}]}
            """);
        var loaded = Preferences.Load(File1);
        Assert.Equal("dark", loaded.Theme);
        Assert.Equal(2, loaded.Glass);
        Assert.Equal(1.5, loaded.UiScale);
        Assert.Equal("F11", loaded.KeyboardShortcut);
        Assert.Equal(ControllerButtons.View | ControllerButtons.Y, loaded.ControllerShortcut);
        Assert.Equal("", loaded.Language);
        var game = Assert.Single(loaded.Manual);
        Assert.Equal(GameSource.Manual, game.Source);
        Assert.False(string.IsNullOrEmpty(game.Id));
    }

    [Fact]
    public void An_unreadable_file_throws_instead_of_being_replaced()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(File1, "{ broken");
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Preferences.Load(File1));
    }

    [Fact]
    public void Pictures_no_game_uses_are_pruned()
    {
        var art = Path.Combine(_folder, "art");
        Directory.CreateDirectory(art);
        var source = Path.Combine(_folder, "cover.jpg");
        File.WriteAllText(source, "x");
        var kept = Preferences.StoreImage(art, "g", "card", source);
        var orphan = Preferences.StoreImage(art, "old", "card", source);
        var preferences = new Preferences();
        preferences.Art["g"] = new Artwork { Card = kept };
        Assert.Equal(1, preferences.PruneArt(art));
        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(orphan));
    }

    [Fact]
    public void Screenshot_combination_defaults_and_never_overlaps_the_home_one()
    {
        var fresh = new Preferences();
        fresh.Normalize();
        Assert.Equal(ShortcutChoices.DefaultScreenshot, fresh.ScreenshotShortcut);
        var unknown = new Preferences { ScreenshotShortcut = 12345 };
        unknown.Normalize();
        Assert.Equal(ShortcutChoices.DefaultScreenshot, unknown.ScreenshotShortcut);
        Assert.All(ShortcutChoices.Screenshot.Where(mask => mask != 0), shot =>
            Assert.All(ShortcutChoices.Controller.Where(mask => mask != 0), home => Assert.NotEqual(home, shot & home)));
    }
}