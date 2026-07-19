using MacroEngine.Core;
using Xunit;

namespace MacroEngine.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "MacroEngine.Tests", Guid.NewGuid().ToString("N"));

    public PersistenceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void AtomicJsonFile_SecondSaveCreatesLastKnownGoodBackup()
    {
        string path = Path.Combine(_directory, "values.json");

        AtomicJsonFile.Save(path, new List<string> { "first" });
        AtomicJsonFile.Save(path, new List<string> { "second" });

        Assert.Equal(new[] { "second" }, AtomicJsonFile.Load<List<string>>(path));
        Assert.Equal(new[] { "first" },
            AtomicJsonFile.Load<List<string>>(AtomicJsonFile.BackupPath(path)));
    }

    [Fact]
    public void TriggerConfig_CorruptMainFileRecoversWithoutCorruptingBackup()
    {
        string path = Path.Combine(_directory, "triggers.json");
        using var config = new TriggerConfig(path);

        var first = new List<TriggerEntry>
        {
            new() { Trigger = "!first", Value = "one", Context = "*", Action = "text" }
        };
        var second = new List<TriggerEntry>
        {
            new() { Trigger = "!second", Value = "two", Context = "*", Action = "text" }
        };

        config.Save(first);
        config.Save(second);
        File.WriteAllText(path, "{ definitely not json");

        List<TriggerEntry> recovered = config.Load();

        Assert.Single(recovered);
        Assert.Equal("!first", recovered[0].Trigger);
        Assert.Equal("!first", AtomicJsonFile.Load<List<TriggerEntry>>(path)[0].Trigger);
        Assert.Equal("!first",
            AtomicJsonFile.Load<List<TriggerEntry>>(AtomicJsonFile.BackupPath(path))[0].Trigger);
    }

    [Fact]
    public void MacroLibrary_RoundTripsAndIndexesNamesCaseInsensitively()
    {
        string path = Path.Combine(_directory, "macros.json");
        using var library = new MacroLibrary(path);
        library.Save(new[]
        {
            new MacroDef { Name = "Save_All", Steps = new List<string> { "key Ctrl+S" } }
        });

        List<MacroDef> loaded = library.Load();

        Assert.Single(loaded);
        Assert.True(library.TryGet("save_all", out MacroDef macro));
        Assert.Equal("key Ctrl+S", macro.Script);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
