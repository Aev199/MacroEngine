using System.Diagnostics;

namespace MacroEngine.Core;

/// <summary>Small checked helpers for opening local folders, files and web pages.</summary>
internal static class ShellTools
{
    public static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Open(path);
    }

    public static void OpenFileOrParent(string path)
    {
        if (File.Exists(path))
        {
            Open(path);
            return;
        }

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException($"Не удалось определить папку для '{path}'.");

        OpenDirectory(directory);
    }

    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException("Некорректная ссылка.");
        }

        Open(uri.AbsoluteUri);
    }

    private static void Open(string target)
    {
        Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });

        if (process == null)
            throw new InvalidOperationException($"Windows не смогла открыть '{target}'.");

        process.Dispose();
    }
}
