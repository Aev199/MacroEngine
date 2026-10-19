using Avalonia;

namespace MacroEngine;

static class Program
{
    private static Mutex? _instanceMutex;

    /// <summary>
    ///  MacroEngine — text expansion & macro automation tool.
    ///  Runs in the system tray, intercepts keyboard globally,
    ///  and expands triggers like @@, !tel, etc. into full text.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // Single instance — a second copy would install a second keyboard hook.
        _instanceMutex = new Mutex(initiallyOwned: true, "MacroEngine_SingleInstance_2F1A", out bool owned);

        // After a self-update the previous process is still shutting down: wait for it.
        if (!owned && args.Contains(UI.AppController.UpdateRestartArgument))
        {
            try { owned = _instanceMutex.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { owned = true; }
        }

        if (!owned)
        {
            System.Windows.Forms.MessageBox.Show("MacroEngine уже запущен.", "MacroEngine",
                System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args,
            Avalonia.Controls.ShutdownMode.OnExplicitShutdown);

        GC.KeepAlive(_instanceMutex);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<UI.App>()
            .UsePlatformDetect()
            .LogToTrace();
}
