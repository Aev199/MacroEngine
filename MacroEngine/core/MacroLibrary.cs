namespace MacroEngine.Core;

/// <summary>A named, reusable macro: a name plus its ordered step lines.</summary>
internal sealed class MacroDef
{
    public string Name { get; set; } = string.Empty;
    public List<string> Steps { get; set; } = new();
    public string Script => string.Join("\n", Steps);
}

/// <summary>
/// Loads and watches macros.json. Saving is validated and atomic; a
/// last-known-good .bak file is retained and used for recovery.
/// </summary>
internal sealed class MacroLibrary : IDisposable
{
    private readonly string _path;
    private readonly object _reloadLock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private bool _disposed;

    private Dictionary<string, MacroDef> _byName = new(StringComparer.OrdinalIgnoreCase);

    public MacroLibrary(string path)
    {
        _path = path;
    }

    public string[] Names => _byName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    public bool TryGet(string name, out MacroDef def) => _byName.TryGetValue(name, out def!);

    public List<MacroDef> Load()
    {
        if (!File.Exists(_path))
        {
            _byName = new(StringComparer.OrdinalIgnoreCase);
            return new List<MacroDef>();
        }

        try
        {
            var list = AtomicJsonFile.Load<List<MacroDef>>(_path);
            Index(list);
            return list;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Macro library load failed: {ex.GetType().Name}: {ex.Message}");

            if (AtomicJsonFile.TryLoadBackup<List<MacroDef>>(_path, out var backup))
            {
                AppLog.Write("Macro library recovered from backup");
                Index(backup);
                try { AtomicJsonFile.Save(_path, backup); }
                catch (Exception restoreEx)
                {
                    AppLog.Write($"Macro library backup restore failed: {restoreEx.GetType().Name}: {restoreEx.Message}");
                }
                return backup;
            }

            _byName = new(StringComparer.OrdinalIgnoreCase);
            return new List<MacroDef>();
        }
    }

    /// <summary>Save macros or throw when the data cannot be persisted safely.</summary>
    public void Save(IEnumerable<MacroDef> macros)
    {
        var list = macros.ToList();
        AtomicJsonFile.Save(_path, list);
        Index(list);
    }

    public void StartWatching()
    {
        string? dir = Path.GetDirectoryName(_path);
        string file = Path.GetFileName(_path);
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
            _reloadTimer?.Change(180, Timeout.Infinite);
    }

    private void ReloadFromWatcher()
    {
        if (_disposed) return;
        try { Load(); }
        catch (Exception ex)
        {
            AppLog.Write($"Macro library hot-reload failed: {ex.GetType().Name}: {ex.Message}");
        }
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
        _reloadTimer?.Dispose();
    }
}
