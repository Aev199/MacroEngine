namespace MacroEngine.Core;

/// <summary>
/// Loads and watches the triggers.json configuration file.
/// Saving is validated and atomic; a last-known-good .bak file is retained.
/// </summary>
internal sealed class TriggerConfig : IDisposable
{
    private readonly string _configPath;
    private readonly object _reloadLock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private bool _disposed;

    public event Action<List<TriggerEntry>>? ConfigChanged;

    public TriggerConfig(string configPath)
    {
        _configPath = configPath;
    }

    public List<TriggerEntry> Load()
    {
        if (!File.Exists(_configPath))
        {
            var defaults = GetDefaultTriggers();
            Save(defaults);
            return defaults;
        }

        try
        {
            return Normalize(AtomicJsonFile.LoadStable<List<TriggerEntry>>(_configPath));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Trigger config load failed: {ex.GetType().Name}: {ex.Message}");

            if (AtomicJsonFile.TryRestoreBackup<List<TriggerEntry>>(_configPath, out var backup))
            {
                AppLog.Write("Trigger config recovered from backup; valid backup preserved");
                return Normalize(backup);
            }

            return new List<TriggerEntry>();
        }
    }

    /// <summary>Save triggers or throw when the data cannot be persisted safely.</summary>
    public void Save(List<TriggerEntry> triggers) =>
        AtomicJsonFile.Save(_configPath, Normalize(triggers));

    public void StartWatching()
    {
        string? dir = Path.GetDirectoryName(_configPath);
        string file = Path.GetFileName(_configPath);
        if (dir == null) return;

        Directory.CreateDirectory(dir);
        _reloadTimer ??= new Timer(_ => ReloadFromWatcher(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        _watcher.Changed += (_, _) => ScheduleReload();
        _watcher.Created += (_, _) => ScheduleReload();
        _watcher.Renamed += (_, _) => ScheduleReload();
    }

    private void ScheduleReload()
    {
        lock (_reloadLock)
            _reloadTimer?.Change(350, Timeout.Infinite);
    }

    private void ReloadFromWatcher()
    {
        if (_disposed) return;
        try
        {
            var triggers = Load();
            ConfigChanged?.Invoke(triggers);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Trigger config hot-reload failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static List<TriggerEntry> GetDefaultTriggers() =>
    [
        new() { Trigger = "@@", Value = "your_email@domain.com", Context = "*", Action = "text" },
        new() { Trigger = "!tel", Value = "+7 (999) 123-45-67", Context = "*", Action = "text" },
        new() { Trigger = "!date", Value = "{date}", Context = "*", Action = "text" },
        new() { Trigger = "!sig", Value = "С уважением,\nИван Иванов\nООО «ПроектСтрой»", Context = "*", Action = "text" },
        new() { Trigger = "!path", Value = @"\\server\projects\2026\", Context = "*", Action = "text" },
        new() { Trigger = "!db", Value = "_MYBEAMPLUGIN", Context = "acad", Action = "text" },
        new() { Trigger = "!beam", Value = "C:\\lisp\\my_beam_routines.lsp", Context = "acad", Action = "lisp" },
        new() { Trigger = "!vb", Value = "python C:\\scripts\\midas_virtual_beams.py", Context = "midas", Action = "script", Hotkey = "Ctrl+Shift+M" },
    ];

    private static List<TriggerEntry> Normalize(List<TriggerEntry> entries)
    {
        var result = new List<TriggerEntry>(entries.Count);
        foreach (TriggerEntry? entry in entries)
        {
            if (entry == null)
                continue;

            entry.Trigger ??= string.Empty;
            entry.Value ??= string.Empty;
            entry.Context ??= "*";
            entry.Action ??= "text";
            result.Add(entry);
        }

        if (result.Count != entries.Count)
            AppLog.Write("Trigger config contained null entries; invalid rows were ignored");

        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
        _reloadTimer?.Dispose();
    }
}
