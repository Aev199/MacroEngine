using System.Text.Json;

namespace MacroEngine.Core;

/// <summary>
/// Loads and watches the triggers.json configuration file.
/// Supports hot-reload via file system watcher.
/// </summary>
internal sealed class TriggerConfig : IDisposable
{
    private readonly string _configPath;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    /// <summary>Fired when the config file changes and is reloaded.</summary>
    public event Action<List<TriggerEntry>>? ConfigChanged;

    public TriggerConfig(string configPath)
    {
        _configPath = configPath;
    }

    /// <summary>Load triggers from the JSON file. Returns empty list if file doesn't exist.</summary>
    public List<TriggerEntry> Load()
    {
        if (!File.Exists(_configPath))
        {
            // Create default config
            var defaults = GetDefaultTriggers();
            Save(defaults);
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(_configPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var triggers = JsonSerializer.Deserialize<List<TriggerEntry>>(json, options);
            return triggers ?? new List<TriggerEntry>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TriggerConfig] Failed to load: {ex.Message}");
            return new List<TriggerEntry>();
        }
    }

    /// <summary>Save triggers to the JSON file.</summary>
    public void Save(List<TriggerEntry> triggers)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
            string json = JsonSerializer.Serialize(triggers, options);
            string? dir = Path.GetDirectoryName(_configPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TriggerConfig] Failed to save: {ex.Message}");
        }
    }

    /// <summary>Start watching the config file for changes (hot-reload).</summary>
    public void StartWatching()
    {
        string? dir = Path.GetDirectoryName(_configPath);
        string file = Path.GetFileName(_configPath);

        if (dir == null || !Directory.Exists(dir))
            return;

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += (_, _) =>
        {
            // Small delay to let the file finish writing
            Thread.Sleep(150);
            var triggers = Load();
            ConfigChanged?.Invoke(triggers);
        };
    }

    private static List<TriggerEntry> GetDefaultTriggers()
    {
        return new List<TriggerEntry>
        {
            new() { Trigger = "@@", Value = "your_email@domain.com", Context = "*", Action = "text" },
            new() { Trigger = "!tel", Value = "+7 (999) 123-45-67", Context = "*", Action = "text" },
            new() { Trigger = "!date", Value = DateTime.Now.ToString("dd.MM.yyyy"), Context = "*", Action = "text" },
            new() { Trigger = "!sig", Value = "С уважением,\nИван Иванов\nООО «ПроектСтрой»", Context = "*", Action = "text" },
            new() { Trigger = "!path", Value = @"\\server\projects\2026\", Context = "*", Action = "text" },
            new() { Trigger = "!db", Value = "_MYBEAMPLUGIN", Context = "acad", Action = "text" },
            new() { Trigger = "!beam", Value = "C:\\lisp\\my_beam_routines.lsp", Context = "acad", Action = "lisp" },
            new() { Trigger = "!vb", Value = "python C:\\scripts\\midas_virtual_beams.py", Context = "midas", Action = "script", Hotkey = "Ctrl+Shift+M" },
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
    }
}
