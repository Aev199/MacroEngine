using System.Diagnostics;
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
        bool previousSuppression = KeyInterceptor.IsSuppressed;
        KeyInterceptor.IsSuppressed = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SplitCommand(command, out string fileName, out string arguments);

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

            long stdoutChars = 0;
            long stderrChars = 0;
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Interlocked.Add(ref stdoutChars, e.Data.Length + Environment.NewLine.Length);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Interlocked.Add(ref stderrChars, e.Data.Length + Environment.NewLine.Length);
            };
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

            process.WaitForExit();

            AppLog.Write($"Script completed with exit code {process.ExitCode}");
            if (Interlocked.Read(ref stdoutChars) > 0)
                AppLog.Diagnostic($"Script stdout captured: chars={Interlocked.Read(ref stdoutChars)}");
            if (Interlocked.Read(ref stderrChars) > 0)
                AppLog.Diagnostic($"Script stderr captured: chars={Interlocked.Read(ref stderrChars)}");

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Script exited with code {process.ExitCode}.");
        }
        finally
        {
            KeyInterceptor.IsSuppressed = previousSuppression;
        }
    }

    internal static void SplitCommand(string command, out string fileName, out string arguments) =>
        CommandLineParser.Split(command, out fileName, out arguments);

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { }
    }
}
