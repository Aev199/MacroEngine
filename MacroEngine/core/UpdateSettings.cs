using System.Text.Json;

namespace MacroEngine.Core;

/// <summary>Persisted updater preferences and bookkeeping (state\update.json).</summary>
internal sealed class UpdateSettings
{
    /// <summary>Check GitHub for a new version on start (at most once a day).</summary>
    public bool AutoCheck { get; set; } = true;

    public DateTimeOffset? LastCheck { get; set; }

    /// <summary>Version installed on disk and waiting for a restart; shown once after it.</summary>
    public string? InstalledVersion { get; set; }

    public static UpdateSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.UpdateStateFile))
                return JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(AppPaths.UpdateStateFile)) ?? new();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Update settings unreadable, using defaults: {ex.GetType().Name}");
        }
        return new UpdateSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.StateDirectory);
            File.WriteAllText(AppPaths.UpdateStateFile, JsonSerializer.Serialize(this));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Update settings save failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
