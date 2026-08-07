using MacroEngine.Core;
using Xunit;

namespace MacroEngine.Tests;

public sealed class SafetyRulesTests
{
    [Fact]
    public void ForegroundTracker_ClearsWhenExactWindowChanges()
    {
        var tracker = new ForegroundWindowTracker();

        Assert.False(tracker.Update(new IntPtr(100)));
        Assert.False(tracker.Update(new IntPtr(100)));
        Assert.True(tracker.Update(new IntPtr(200)));
        Assert.False(tracker.Update(IntPtr.Zero));
        Assert.False(tracker.Update(new IntPtr(200)));
    }

    [Theory]
    [InlineData("Ctrl+K")]
    [InlineData("Alt+Shift+9")]
    [InlineData("F1")]
    [InlineData("Shift+F24")]
    public void SupportedShortcuts_AreAccepted(string shortcut)
    {
        Assert.True(HotkeyRules.TryValidateShortcut(shortcut, out string error), error);
    }

    [Theory]
    [InlineData("K")]
    [InlineData("Shift+K")]
    [InlineData("Win+K")]
    [InlineData("Ctrl+A+B")]
    public void ShortcutsThatRuntimeCannotExecute_AreRejected(string shortcut)
    {
        Assert.False(HotkeyRules.TryValidateShortcut(shortcut, out _));
    }

    [Theory]
    [InlineData("Ctrl+Alt")]
    [InlineData("Shift+Ctrl")]
    [InlineData("Alt+Ctrl+Shift")]
    public void SupportedLeaders_AreAccepted(string leader)
    {
        Assert.True(HotkeyRules.TryValidateLeader(leader, out string error), error);
    }

    [Theory]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Win")]
    [InlineData("Ctrl+Alt+K")]
    public void InvalidLeaders_AreRejected(string leader)
    {
        Assert.False(HotkeyRules.TryValidateLeader(leader, out _));
    }

    [Fact]
    public void HotkeyNormalization_CanonicalizesModifierOrderAndAliases()
    {
        Assert.Equal("ctrl+alt+shift+k", HotkeyRules.Normalize(" Shift + Control + Alt + K "));
        Assert.True(SystemHotkeys.IsSystem("Shift+Ctrl+Escape"));
    }

    [Fact]
    public void InputBuffer_MatchesEquivalentModifierOrder()
    {
        var buffer = new InputBuffer();
        buffer.LoadTriggers(
        [
            new TriggerEntry
            {
                Hotkey = "Shift+Ctrl+K",
                Action = "text",
                Value = "ok"
            }
        ]);

        TriggerEntry? match = buffer.MatchHotkey("Ctrl+Shift+K");

        Assert.NotNull(match);
        Assert.Equal("ok", match.Value);
    }

    [Theory]
    [InlineData(0x08, false)] // Backspace is not an extended key.
    [InlineData(0x0D, false)]
    [InlineData(0x25, true)]
    [InlineData(0x2E, true)]
    public void ExtendedKeyFlag_IsAppliedOnlyWhereWindowsRequiresIt(int vk, bool expected)
    {
        Assert.Equal(expected, InputInjection.RequiresExtendedKeyFlag((ushort)vk));
    }
}
