using Avalonia.Threading;
using MacroEngine.Core;

namespace MacroEngine.UI;

internal enum UpdateStage { Idle, Checking, UpToDate, Available, Downloading, Installed, Failed }

/// <summary>
/// Update state shared by the About window and the tray menu. All members are used
/// on the UI thread; <see cref="Changed"/> is raised there too.
///
/// Channel: a beta build follows pre-releases as well, a stable build only releases.
/// </summary>
internal sealed class UpdateCoordinator
{
    private static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(20);
    private static readonly TimeSpan AutoCheckDelay = TimeSpan.FromSeconds(20);

    private readonly UpdateSettings _settings = UpdateSettings.Load();
    private readonly Func<bool> _restart;
    private CancellationTokenSource? _cts;

    public UpdateCoordinator(Func<bool> restart)
    {
        _restart = restart;
        JustUpdated = _settings.InstalledVersion != null
                      && SemVersion.TryParse(_settings.InstalledVersion)?.Equals(Current) == true;
        if (_settings.InstalledVersion != null)
        {
            _settings.InstalledVersion = null;
            _settings.Save();
        }
    }

    public static SemVersion Current { get; } = SemVersion.TryParse(ProductInfo.Version) ?? SemVersion.TryParse("0.0.0")!;

    public UpdateStage Stage { get; private set; } = UpdateStage.Idle;
    public ReleaseInfo? Release { get; private set; }
    public double Progress { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? LastCheck => _settings.LastCheck;

    /// <summary>This run is the first start of a version installed by the updater.</summary>
    public bool JustUpdated { get; }

    public bool AutoCheck
    {
        get => _settings.AutoCheck;
        set
        {
            if (_settings.AutoCheck == value) return;
            _settings.AutoCheck = value;
            _settings.Save();
            Changed?.Invoke();
        }
    }

    public bool Busy => Stage is UpdateStage.Checking or UpdateStage.Downloading;

    public event Action? Changed;

    /// <summary>Quiet daily check shortly after start; failures stay silent until asked.</summary>
    public void StartAutoCheck()
    {
        if (!AutoCheck) return;
        if (LastCheck is { } last && DateTimeOffset.UtcNow - last < AutoCheckInterval) return;
        DispatcherTimer.RunOnce(() => _ = CheckAsync(), AutoCheckDelay);
    }

    public async Task CheckAsync()
    {
        if (Busy || Stage == UpdateStage.Installed) return;
        Set(UpdateStage.Checking);
        try
        {
            Release = await UpdateService.CheckAsync(Current, Current.IsPrerelease, Cts().Token);
            _settings.LastCheck = DateTimeOffset.UtcNow;
            _settings.Save();
            Set(Release != null ? UpdateStage.Available : UpdateStage.UpToDate);
        }
        catch (Exception ex) when (ex is UpdateException or OperationCanceledException or System.Text.Json.JsonException)
        {
            Fail(ex is UpdateException ? ex.Message : "Не удалось прочитать список релизов.", ex);
        }
    }

    public async Task InstallAsync()
    {
        if (Release == null || Busy) return;
        Progress = 0;
        Set(UpdateStage.Downloading);
        var progress = new Progress<double>(p => { Progress = p; Changed?.Invoke(); });
        try
        {
            string zip = await UpdateService.DownloadAsync(Release, progress, Cts().Token);
            await Task.Run(() => UpdateService.InstallZip(zip, AppPaths.ApplicationDirectory));
            _settings.InstalledVersion = Release.Version.ToString();
            _settings.Save();
            AppLog.Write($"Update {Release.Version} installed; restart pending");
            Set(UpdateStage.Installed);
        }
        catch (OperationCanceledException ex)
        {
            Fail("Загрузка отменена.", ex);
        }
        catch (Exception ex) when (ex is UpdateException or IOException or UnauthorizedAccessException)
        {
            Fail(ex is UpdateException ? ex.Message : "Не удалось установить обновление: " + ex.Message, ex);
        }
    }

    /// <summary>False while the editor is open (it is brought forward instead).</summary>
    public bool Restart() => Stage == UpdateStage.Installed && _restart();

    public void Cancel() => _cts?.Cancel();

    private CancellationTokenSource Cts()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return _cts;
    }

    private void Set(UpdateStage stage)
    {
        Stage = stage;
        if (stage != UpdateStage.Failed) Error = null;
        Changed?.Invoke();
    }

    private void Fail(string message, Exception ex)
    {
        AppLog.Write($"Update failed: {ex.GetType().Name}: {ex.Message}");
        Error = message;
        Set(UpdateStage.Failed);
    }
}
