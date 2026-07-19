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

    public static bool TryLoadBackup<T>(string path, out T value)
    {
        string backup = BackupPath(path);
        if (!File.Exists(backup))
        {
            value = default!;
            return false;
        }

        try
        {
            value = Load<T>(backup);
            return true;
        }
        catch
        {
            value = default!;
            return false;
        }
    }

    public static void Save<T>(string path, T value)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException($"Cannot determine directory for '{path}'.");

        Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(value, WriteOptions);
        _ = Deserialize<T>(json, path); // validate before touching the current file

        string temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        string backup = BackupPath(path);

        try
        {
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // Flush file contents before replacing the last-known-good config.
            using (var stream = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                stream.Flush(flushToDisk: true);

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temp, path, backup, ignoreMetadataErrors: true);
                    temp = string.Empty;
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    // Fall through to the portable replacement path.
                }
                catch (IOException)
                {
                    // Some filesystems do not support Replace; preserve a backup manually.
                }

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
        }
    }

    public static string BackupPath(string path) => path + ".bak";

    private static T Deserialize<T>(string json, string source)
    {
        T? value = JsonSerializer.Deserialize<T>(json, ReadOptions);
        return value ?? throw new InvalidDataException($"JSON file '{source}' contains null instead of {typeof(T).Name}.");
    }
}
