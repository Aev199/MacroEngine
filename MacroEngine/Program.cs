using MacroEngine.UI;

namespace MacroEngine;

static class Program
{
    /// <summary>
    ///  MacroEngine — text expansion & macro automation tool.
    ///  Runs in the system tray, intercepts keyboard globally,
    ///  and expands triggers like @@, !tel, etc. into full text.
    /// </summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}