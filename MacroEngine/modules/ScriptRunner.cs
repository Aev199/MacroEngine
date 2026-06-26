using System.Diagnostics;
using System.Text;

namespace MacroEngine.Modules;

/// <summary>
/// Executes external scripts and commands triggered by action="script".
/// </summary>
internal static class ScriptRunner
{
    /// <summary>
    /// Run a command string. Supports:
    ///   python C:\scripts\myscript.py
    ///   C:\tools\backup.bat
    ///   powershell -File C:\scripts\deploy.ps1
    ///   notepad.exe
    /// 
    /// Tokens {date} etc. are already resolved before calling this method.
    /// </summary>
    public static void Run(string command, string triggerName)
    {
        try
        {
            // Split command into file name and arguments
            string fileName;
            string arguments;

            if (command.StartsWith('"'))
            {
                // Quoted path: "C:\Program Files\app.exe" args
                int endQuote = command.IndexOf('"', 1);
                if (endQuote > 1)
                {
                    fileName = command[1..endQuote];
                    arguments = command[(endQuote + 1)..].Trim();
                }
                else
                {
                    fileName = command;
                    arguments = "";
                }
            }
            else
            {
                int spaceIdx = command.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    fileName = command[..spaceIdx];
                    arguments = command[(spaceIdx + 1)..].Trim();
                }
                else
                {
                    fileName = command;
                    arguments = "";
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(fileName) ?? ""
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                Log($"[Script] FAILED to start: {command}");
                return;
            }

            // Read stdout/stderr asynchronously. Reading after WaitForExit (or with
            // synchronous ReadToEnd before it) can deadlock when the child fills the
            // pipe buffer, so drain via events while the process runs.
            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
            process.ErrorDataReceived  += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(30_000)) // 30 second timeout
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                Log($"[Script] TIMEOUT [{triggerName}] killed after 30s: {command}");
                return;
            }
            process.WaitForExit(); // let async readers flush remaining output

            string stdout = sbOut.ToString().Trim();
            string stderr = sbErr.ToString().Trim();

            if (process.ExitCode == 0)
            {
                Log($"[Script] OK [{triggerName}] exit=0 {(stdout.Length > 0 ? "out=" + Truncate(stdout, 200) : "")}");
            }
            else
            {
                Log($"[Script] ERROR [{triggerName}] exit={process.ExitCode} {(stderr.Length > 0 ? "err=" + Truncate(stderr, 200) : "")}");
            }
        }
        catch (Exception ex)
        {
            Log($"[Script] EXCEPTION [{triggerName}]: {ex.Message}");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    private static void Log(string message)
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "macroengine.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch { }
    }
}
