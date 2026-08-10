using MacroEngine.Core;
using MacroEngine.UI;
using Xunit;

namespace MacroEngine.Tests;

public sealed class SettingsStateTests
{
    [Fact]
    public void TriggerFingerprint_EqualModelsAreClean()
    {
        var loaded = new[]
        {
            new TriggerEntry
            {
                Trigger = "!mail",
                Value = "name@example.com",
                Context = "*",
                Action = "text"
            },
            new TriggerEntry
            {
                Trigger = "",
                Hotkey = "Ctrl+Alt+M",
                Value = "Save_All",
                Context = "acad",
                Action = "macro"
            }
        };
        var current = new[]
        {
            new TriggerEntry
            {
                Trigger = "!mail",
                Value = "name@example.com",
                Context = "*",
                Action = "text"
            },
            new TriggerEntry
            {
                Trigger = "",
                Hotkey = "Ctrl+Alt+M",
                Value = "Save_All",
                Context = "acad",
                Action = "macro"
            }
        };

        Assert.Equal(
            SettingsStateFingerprint.Triggers(loaded),
            SettingsStateFingerprint.Triggers(current));
    }

    [Fact]
    public void TriggerFingerprint_RealEditChangesState()
    {
        var entry = new TriggerEntry
        {
            Trigger = "!mail",
            Value = "name@example.com",
            Context = "*",
            Action = "text"
        };
        string saved = SettingsStateFingerprint.Triggers(new[] { entry });

        entry.Context = "acad";

        Assert.NotEqual(saved, SettingsStateFingerprint.Triggers(new[] { entry }));
    }

    [Fact]
    public void MacroFingerprint_RealEditChangesState()
    {
        var macro = new MacroDef
        {
            Name = "Save_All",
            Steps = new List<string> { "key Ctrl+S" }
        };
        string saved = SettingsStateFingerprint.Macros(new[] { macro });

        macro.Steps.Add("sleep 100");

        Assert.NotEqual(saved, SettingsStateFingerprint.Macros(new[] { macro }));
    }
}
