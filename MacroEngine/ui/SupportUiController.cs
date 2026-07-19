using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>
/// Adds non-critical help and support commands after the main tray controller has
/// been initialized. It deliberately does not participate in keyboard execution.
/// </summary>
internal sealed class SupportUiController : IDisposable
{
    private HelpWindow? _helpWindow;
    private AboutWindow? _aboutWindow;
    private bool _disposed;

    public SupportUiController()
    {
        AttachTrayItems();

        if (!File.Exists(AppPaths.HelpFirstRunMarker))
        {
            try
            {
                Directory.CreateDirectory(AppPaths.StateDirectory);
                File.WriteAllText(AppPaths.HelpFirstRunMarker, ProductInfo.Version);
            }
            catch (Exception ex)
            {
                AppLog.Write($"Help first-run marker failed: {ex.GetType().Name}: {ex.Message}");
            }

            Dispatcher.UIThread.Post(OpenHelp);
        }
    }

    private void AttachTrayItems()
    {
        TrayIcons? icons = TrayIcon.GetIcons(Application.Current!);
        TrayIcon? tray = icons?.FirstOrDefault();
        NativeMenu? menu = tray?.Menu;
        if (menu == null)
        {
            AppLog.Write("Support UI could not find the main tray menu");
            return;
        }

        var version = new NativeMenuItem($"MacroEngine {ProductInfo.DisplayVersion}")
        {
            IsEnabled = false
        };
        var help = new NativeMenuItem("Справка…");
        var data = new NativeMenuItem("Открыть папку данных");
        var about = new NativeMenuItem("О программе…");

        help.Click += (_, _) => OpenHelp();
        data.Click += (_, _) => OpenDataFolder();
        about.Click += (_, _) => OpenAbout();

        // The main controller keeps the final separator + Exit item at the end.
        int insertAt = Math.Max(0, menu.Items.Count - 2);
        menu.Items.Insert(insertAt++, new NativeMenuItemSeparator());
        menu.Items.Insert(insertAt++, version);
        menu.Items.Insert(insertAt++, help);
        menu.Items.Insert(insertAt++, data);
        menu.Items.Insert(insertAt, about);
    }

    private void OpenHelp()
    {
        if (_disposed) return;
        if (_helpWindow != null)
        {
            _helpWindow.Activate();
            return;
        }

        _helpWindow = new HelpWindow(OpenAbout);
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Show();
        _helpWindow.Activate();
    }

    private void OpenAbout()
    {
        if (_disposed) return;
        if (_aboutWindow != null)
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow(OpenHelp);
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    private static void OpenDataFolder()
    {
        try
        {
            ShellTools.OpenDirectory(AppPaths.RootDirectory);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Open data directory failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _helpWindow?.Close();
        _aboutWindow?.Close();
    }
}
