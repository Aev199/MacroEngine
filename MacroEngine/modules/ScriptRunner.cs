using System.Diagnostics;
using System.Text;
using MacroEngine.Core;

namespace MacroEngine.Modules;

/// <summary>Executes external scripts and commands triggered by action="script".</summary>
internal static class ScriptRunner
{
    public static void Run(
        string command,
        string triggerName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string fileName;
        string arguments;
        SplitCommand(command, out fileName, out arguments);

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

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("The script process could not be started.");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!process.WaitForExit(100))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                KillProcess(process);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (DateTime.UtcNow >= deadline)
            {
                KillProcess(process);
                throw new TimeoutException("Script execution exceeded 30 seconds.");
            }
        }

        process.WaitForExit(); // allow asynchronous output readers to flush

        AppLog.Write($"Script completed with exit code {process.ExitCode}");
        if (stdout.Length > 0)
            AppLog.Diagnostic("Script stdout: " + Truncate(stdout.ToString().Trim(), 500));
        if (stderr.Length > 0)
            AppLog.Diagnostic("Script stderr: " + Truncate(stderr.ToString().Trim(), 500));

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Script exited with code {process.ExitCode}.");
    }

    internal static void SplitCommand(string command, out string fileName, out string arguments)
    {
        if (command.StartsWith('"'))
        {
            int endQuote = command.IndexOf('"', 1);
            if (endQuote > 1)
            {
                fileName = command[1..endQuote];
                arguments = command[(endQuote + 1)..].Trim();
                return;
            }
        }

        int spaceIndex = command.IndexOf(' ');
        if (spaceIndex > 0)
        {
            fileName = command[..spaceIndex];
            arguments = command[(spaceIndex + 1)..].Trim();
        }
        else
        {
            fileName = command.Trim('"');
            arguments = "";
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
