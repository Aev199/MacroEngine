using System.Text;
using System.Text.Json;

namespace MacroEngine.Core;

/// <summary>Validated, atomic JSON persistence with a single last-known-good backup.</summary>
internal static class AtomicJsonFile
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static T Load<T>(string path)
    {
        string json = File.ReadAllText(path, Encoding.UTF8);
        return Deserialize<T>(json, path);
    }

    /// <summary>
    /// Retry reads to tolerate the short incomplete-file window produced by
    /// external editors that rewrite JSON in place.
    /// </summary>
    public static T LoadStable<T>(string path, int attempts = 5, int delayMilliseconds = 120)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                return Load<T>(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                lastError = ex;
                if (attempt + 1 < attempts)
                    Thread.Sleep(delayMilliseconds);
            }
        }

        throw lastError ?? new InvalidDataException($"Cannot read JSON file '{path}'.");
    }

    public static bool TryRestoreBackup<T>(string path, out T value)
    {
        string backup = BackupPath(path);
        if (!File.Exists(backup))
        {
            value = default!;
            return false;
        }

        try
        {
            value = LoadStable<T>(backup, attempts: 2, delayMilliseconds: 50);
        }
        catch
        {
            value = default!;
            return false;
        }

        try
        {
            WriteValidated(path, value, updateBackup: false);
        }
        catch (Exception ex)
        {
            // The in-memory recovery is still valid and safer than returning an
            // empty configuration merely because the damaged file is read-only.
            AppLog.Write($"Recovered JSON could not be written back: {ex.GetType().Name}: {ex.Message}");
        }

        return true;
    }

    public static void Save<T>(string path, T value) =>
        WriteValidated(path, value, updateBackup: true);

    private static void WriteValidated<T>(string path, T value, bool updateBackup)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException($"Cannot determine directory for '{path}'.");

        Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(value, WriteOptions);
        _ = Deserialize<T>(json, path);

        string temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        string backup = BackupPath(path);
        string discarded = temp + ".old";

        try
        {
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            using (var stream = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                stream.Flush(flushToDisk: true);

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(
                        temp,
                        path,
                        updateBackup ? backup : discarded,
                        ignoreMetadataErrors: true);
                    temp = string.Empty;
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    // Fall through to the portable replacement path.
                }
                catch (IOException)
                {
                    // Some filesystems do not implement Replace.
                }

                if (updateBackup)
                    File.Copy(path, backup, overwrite: true);
            }

            File.Move(temp, path, overwrite: true);
            temp = string.Empty;
        }
        finally
        {
            if (temp.Length > 0)
            {
                try { File.Delete(temp); } catch { }
            }

            try { File.Delete(discarded); } catch { }
        }
    }

    public static string BackupPath(string path) => path + ".bak";

    private static T Deserialize<T>(string json, string source)
    {
        T? value = JsonSerializer.Deserialize<T>(json, ReadOptions);
        return value ?? throw new InvalidDataException(
            $"JSON file '{source}' contains null instead of {typeof(T).Name}.");
    }
}
