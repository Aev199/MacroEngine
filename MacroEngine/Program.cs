using MacroEngine.UI;

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
    static void Main()
    {
        // Single instance — a second copy would install a second keyboard hook.
        _instanceMutex = new Mutex(initiallyOwned: true, "MacroEngine_SingleInstance_2F1A", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("MacroEngine уже запущен.", "MacroEngine",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());

        GC.KeepAlive(_instanceMutex);
    }
}