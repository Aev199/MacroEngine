namespace MacroEngine.Core;

/// <summary>
/// Centralized locations for mutable application data.
/// By default data lives under %LocalAppData%\MacroEngine. Creating a
/// .portable marker next to the executable keeps the legacy portable layout.
/// </summary>
internal static class AppPaths
{
    private static readonly string InstallDirectory = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly bool Portable = File.Exists(Path.Combine(InstallDirectory, ".portable"));

    public static string RootDirectory { get; } = Portable
        ? InstallDirectory
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacroEngine");

    public static string ConfigDirectory { get; } = Path.Combine(RootDirectory, "config");
    public static string LogDirectory { get; } = Path.Combine(RootDirectory, "logs");
    public static string StateDirectory { get; } = Path.Combine(RootDirectory, "state");

    public static string TriggersFile { get; } = Path.Combine(ConfigDirectory, "triggers.json");
    public static string MacrosFile { get; } = Path.Combine(ConfigDirectory, "macros.json");
    public static string LogFile { get; } = Path.Combine(LogDirectory, "macroengine.log");
    public static string FirstRunMarker { get; } = Path.Combine(StateDirectory, ".firstrun");
    public static string DiagnosticMarker { get; } = Path.Combine(StateDirectory, "diagnostic.logging");

    public static bool IsPortable => Portable;

    public static void Initialize()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(StateDirectory);

        if (Portable)
            return;

        // Preserve existing installations and also seed a fresh installation
        // from the config templates shipped beside the executable.
        CopyIfMissing(Path.Combine(InstallDirectory, "config", "triggers.json"), TriggersFile);
        CopyIfMissing(Path.Combine(InstallDirectory, "config", "macros.json"), MacrosFile);

        // Versions before the privacy hardening release wrote every typed key to
        // these legacy files. They are diagnostic artefacts, not user data, and
        // should not remain on disk after an upgrade.
        DeleteLegacyLog(Path.Combine(InstallDirectory, "macroengine.log"));
        DeleteLegacyLog(Path.Combine(InstallDirectory, "macroengine.log.1"));
    }

    private static void CopyIfMissing(string source, string destination)
    {
        if (File.Exists(destination) || !File.Exists(source))
            return;

        try
        {
            File.Copy(source, destination, overwrite: false);
        }
        catch (IOException) when (File.Exists(destination))
        {
            // Another instance or startup path won the race.
        }
    }

    private static void DeleteLegacyLog(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A locked legacy log is harmless; do not block application startup.
        }
    }
}
