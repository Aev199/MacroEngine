using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MacroEngine.Core;

/// <summary>Stable product metadata used by help, diagnostics and About UI.</summary>
internal static class ProductInfo
{
    private static readonly Assembly ProductAssembly = typeof(ProductInfo).Assembly;

    public const string RepositoryUrl = "https://github.com/Aev199/MacroEngine";

    public static string Version
    {
        get
        {
            string? informational = ProductAssembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Split('+', 2)[0];

            return ProductAssembly.GetName().Version?.ToString(3) ?? "development";
        }
    }

    public static string DisplayVersion => $"v{Version}";

    public static string ReadEmbeddedReadme()
    {
        const string expectedName = "MacroEngine.README.md";
        string? resourceName = ProductAssembly.GetManifestResourceNames()
            .FirstOrDefault(name => string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
            ?? ProductAssembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("README.md", StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            return "# MacroEngine\n\nВстроенная справка недоступна в этой сборке.";

        using Stream? stream = ProductAssembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return "# MacroEngine\n\nНе удалось открыть встроенную справку.";

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Build a support-friendly summary without trigger values, clipboard data,
    /// commands, window titles or other user content.
    /// </summary>
    public static string BuildDiagnosticSummary()
    {
        var lines = new[]
        {
            $"MacroEngine {DisplayVersion}",
            $"Created: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            $"OS: {RuntimeInformation.OSDescription}",
            $"Runtime: {RuntimeInformation.FrameworkDescription}",
            $"Process: {(Environment.Is64BitProcess ? "x64" : "x86")}",
            $"Data mode: {(AppPaths.IsPortable ? "portable" : "LocalAppData")}",
            $"Application directory: {AppPaths.ApplicationDirectory}",
            $"Data directory: {AppPaths.RootDirectory}",
            $"Triggers: {AppPaths.TriggersFile}",
            $"Macros: {AppPaths.MacrosFile}",
            $"Log: {AppPaths.LogFile}",
            $"Diagnostic logging: {(AppLog.DiagnosticEnabled ? "enabled" : "disabled")}",
        };

        return string.Join(Environment.NewLine, lines);
    }
}
