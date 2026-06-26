using System.Text.Json;

namespace MacroEngine.Core;

/// <summary>A named, reusable macro: a name plus its ordered step lines.</summary>
internal sealed class MacroDef
{
    public string Name { get; set; } = string.Empty;
    public List<string> Steps { get; set; } = new();

    /// <summary>Steps joined into the newline-separated script MacroRunner expects.</summary>
    public string Script => string.Join("\n", Steps);
}

/// <summary>
/// Loads / saves the named macros library (macros.json) and offers name lookup.
/// Mirrors <see cref="TriggerConfig"/> (System.Text.Json + optional hot-reload).
/// </summary>
internal sealed class MacroLibrary : IDisposable
{
    private readonly string _path;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    private Dictionary<string, MacroDef> _byName = new(StringComparer.OrdinalIgnoreCase);

    public MacroLibrary(string path)
    {
        _path = path;
    }

    /// <summary>Macro names, sorted for display.</summary>
    public string[] Names => _byName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool TryGet(string name, out MacroDef def) => _byName.TryGetValue(name, out def!);

    public List<MacroDef> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var list = JsonSerializer.Deserialize<List<MacroDef>>(json, options) ?? new();
                Index(list);
                return list;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MacroLibrary] Failed to load: {ex.Message}");
        }

        _byName = new(StringComparer.OrdinalIgnoreCase);
        return new();
    }

    public void Save(IEnumerable<MacroDef> macros)
    {
        var list = macros.ToList();
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
            string json = JsonSerializer.Serialize(list, options);
            string? dir = Path.GetDirectoryName(_path);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_path, json);
            Index(list);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MacroLibrary] Failed to save: {ex.Message}");
        }
    }

    /// <summary>Watch macros.json so runtime name lookups stay current after manual edits.</summary>
    public void StartWatching()
    {
        string? dir = Path.GetDirectoryName(_path);
        string file = Path.GetFileName(_path);
        if (dir == null || !Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) => { Thread.Sleep(150); Load(); };
    }

    private void Index(List<MacroDef> list)
    {
        _byName = list
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
    }
}
