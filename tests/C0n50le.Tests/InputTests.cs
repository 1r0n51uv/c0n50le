using C0n50le.Core.Input;

namespace C0n50le.Tests;

public sealed class InputTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-26T18:00:00Z");

    [Fact]
    public void Controller_use_switches_to_controller_mode_and_a_key_switches_back()
    {
        var tracker = new InputModeTracker();
        var changes = new List<InputMode>();
        tracker.Changed += changes.Add;
        tracker.ControllerUsed();
        tracker.ControllerUsed();
        tracker.KeyPressed();
        Assert.Equal([InputMode.Controller, InputMode.MouseKeyboard], changes);
    }

    [Fact]
    public void A_twitching_mouse_does_not_leave_controller_mode()
    {
        var tracker = new InputModeTracker(InputMode.Controller);
        tracker.MouseMoved(100, 100, Start);
        tracker.MouseMoved(104, 102, Start.AddMilliseconds(50));
        tracker.MouseMoved(101, 99, Start.AddMilliseconds(100));
        Assert.Equal(InputMode.Controller, tracker.Mode);
        // Small moves spread over a long time don't add up either.
        for (var i = 1; i <= 10; i++) tracker.MouseMoved(100 + 3 * i, 100, Start.AddSeconds(i));
        Assert.Equal(InputMode.Controller, tracker.Mode);
    }

    [Fact]
    public void A_real_mouse_move_or_click_switches_to_mouse_mode()
    {
        var tracker = new InputModeTracker(InputMode.Controller);
        tracker.MouseMoved(100, 100, Start);
        tracker.MouseMoved(130, 100, Start.AddMilliseconds(60));
        Assert.Equal(InputMode.MouseKeyboard, tracker.Mode);
        tracker.ControllerUsed();
        tracker.MouseClicked();
        Assert.Equal(InputMode.MouseKeyboard, tracker.Mode);
    }

    [Fact]
    public void Held_directions_repeat_after_a_pause()
    {
        var gate = new RepeatGate();
        var right = ControllerButtons.Right;
        Assert.Equal(right, gate.Moves(right, Start));
        Assert.Equal(0, gate.Moves(right, Start.AddMilliseconds(200)));
        Assert.Equal(right, gate.Moves(right, Start.AddMilliseconds(430)));
        Assert.Equal(0, gate.Moves(right, Start.AddMilliseconds(500)));
        Assert.Equal(right, gate.Moves(right, Start.AddMilliseconds(560)));
        Assert.Equal(0, gate.Moves(0, Start.AddMilliseconds(600)));
        Assert.Equal(right, gate.Moves(right, Start.AddMilliseconds(610)));
    }

    [Fact]
    public void Buttons_other_than_directions_never_repeat() =>
        Assert.Equal(0, new RepeatGate().Moves(ControllerButtons.Confirm, Start));

    [Fact]
    public void Shortcut_triggers_once_when_the_whole_combination_is_held()
    {
        var mask = ControllerButtons.View | ControllerButtons.Y;
        Assert.False(ShortcutChoices.Triggered(mask, 0, ControllerButtons.View));
        Assert.True(ShortcutChoices.Triggered(mask, ControllerButtons.View, mask));
        Assert.False(ShortcutChoices.Triggered(mask, mask, mask));
        Assert.True(ShortcutChoices.Triggered(mask, 0, mask | ControllerButtons.Down));
        Assert.False(ShortcutChoices.Triggered(0, 0, mask));
    }

    [Fact]
    public void Glyphs_follow_the_pad_family()
    {
        var mask = ControllerButtons.View | ControllerButtons.Y;
        Assert.Equal("⧉ + Y", ButtonGlyphs.Combination(mask, PadFamily.Xbox));
        Assert.Equal("CREATE + △", ButtonGlyphs.Combination(mask, PadFamily.PlayStation));
        Assert.Equal("✕", ButtonGlyphs.Confirm(PadFamily.PlayStation));
        Assert.Equal("A", ButtonGlyphs.Confirm(PadFamily.Xbox));
    }

    [Fact]
    public void Shortcut_choices_are_known_and_distinct()
    {
        Assert.Equal(ShortcutChoices.Controller.Count, ShortcutChoices.Controller.Distinct().Count());
        Assert.All(ShortcutChoices.Controller.Where(mask => mask != 0),
            mask => Assert.True(System.Numerics.BitOperations.PopCount((uint)mask) >= 2));
        Assert.True(ShortcutChoices.IsKnownKeyboard(""));
        Assert.False(ShortcutChoices.IsKnownKeyboard("F1"));
    }
}
