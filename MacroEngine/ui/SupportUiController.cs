using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>
/// Adds help, About and update commands after the main tray controller has been
/// initialized. It deliberately does not participate in keyboard execution.
/// </summary>
internal sealed class SupportUiController : IDisposable
{
    private readonly UpdateCoordinator _updates;
    private NativeMenuItem? _updateItem;
    private HelpWindow? _helpWindow;
    private AboutWindow? _aboutWindow;
    private bool _disposed;

    public SupportUiController(Func<bool> restartForUpdate)
    {
        _updates = new UpdateCoordinator(restartForUpdate);
        _updates.Changed += RefreshUpdateItem;
        AttachTrayItems();

        // Leftovers of the previous version are unlocked once its process has exited.
        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            UpdateService.CleanupLeftovers(AppPaths.ApplicationDirectory, AppPaths.UpdatesDirectory);
        });

        if (_updates.JustUpdated)
        {
            Dispatcher.UIThread.Post(() => { OpenAbout(); _aboutWindow?.ShowUpdated(); });
        }
        else if (!File.Exists(AppPaths.HelpFirstRunMarker))
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

        _updates.StartAutoCheck();
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
        _updateItem = new NativeMenuItem("Проверить обновления");
        _updateItem.Click += (_, _) => OnUpdateItem();
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
        menu.Items.Insert(insertAt++, _updateItem);
        menu.Items.Insert(insertAt++, help);
        menu.Items.Insert(insertAt++, data);
        menu.Items.Insert(insertAt, about);
    }

    private void OnUpdateItem()
    {
        if (_updates.Stage == UpdateStage.Installed && _updates.Restart()) return;
        OpenAbout();
        if (_updates.Stage is UpdateStage.Idle or UpdateStage.UpToDate or UpdateStage.Failed)
            _ = _updates.CheckAsync();
    }

    private void RefreshUpdateItem()
    {
        if (_updateItem == null) return;
        _updateItem.Header = _updates.Stage switch
        {
            UpdateStage.Checking    => "Проверка обновлений…",
            UpdateStage.Available   => $"Обновить до v{_updates.Release!.Version}…",
            UpdateStage.Downloading => $"Загрузка обновления… {_updates.Progress:P0}",
            UpdateStage.Installed   => "Перезапустить для обновления",
            _                       => "Проверить обновления"
        };
        _updateItem.IsEnabled = !_updates.Busy;
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

        _aboutWindow = new AboutWindow(_updates, OpenHelp);
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
        _updates.Cancel();
        _helpWindow?.Close();
        _aboutWindow?.Close();
    }
}
