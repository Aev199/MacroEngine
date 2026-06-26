using System.Diagnostics;

namespace MacroEngine.Core;

internal static class AppLog
{
    private static readonly string _path =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "macroengine.log");

    private const long MaxBytes = 5 * 1024 * 1024;
    private static int _writeCount;

    public static void Write(string message)
    {
        try
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
            Debug.WriteLine(line);
            File.AppendAllText(_path, line + "\n");

            if (++_writeCount % 50 == 0)
                RotateIfNeeded();
        }
        catch { }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length < MaxBytes) return;

            string backup = _path + ".1";
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(_path, backup);
        }
        catch { }
    }
}
